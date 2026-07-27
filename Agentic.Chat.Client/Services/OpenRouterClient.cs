using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Models;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Services;

public sealed class OpenRouterClient(
    HttpClient http,
    OpenRouterCredentialService credentials,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterClient> logger) : IOpenRouterClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenRouterCredentialService _credentials = credentials;
    private readonly HttpClient _http = http;
    private readonly OpenRouterOptions _options = options.Value;

    public async IAsyncEnumerable<StreamDelta> StreamChatAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var apiKey = await _credentials
            .GetKeyForModelAsync(request.Model)
            .ConfigureAwait(false);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", _options.HttpReferer);
        httpRequest.Headers.TryAddWithoutValidation("X-OpenRouter-Title", _options.AppTitle);

        using var response = await _http
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new OpenRouterException((int)response.StatusCode, body);
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) { yield break; }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) { continue; }
            var payload = line["data:".Length..].Trim();
            if (payload is "[DONE]") { yield break; }
            var delta = DecodeDelta(payload);
            if (delta is not null) { yield return delta; }
        }
    }

    private StreamDelta? DecodeDelta(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (root.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var messageElement)
                    && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString()
                    : "OpenRouter stopped the stream.";
                var code = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("code", out var codeElement)
                    && codeElement.ValueKind == JsonValueKind.Number
                    && codeElement.TryGetInt32(out var parsedCode)
                        ? parsedCode
                        : 500;
                throw new OpenRouterException(code, message ?? "OpenRouter stopped the stream.");
            }

            var usage = ParseUsage(root);
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || choices[0].ValueKind != JsonValueKind.Object
                || !choices[0].TryGetProperty("delta", out var delta)
                || delta.ValueKind != JsonValueKind.Object)
            {
                return usage is null ? null : new StreamDelta(null, null, usage);
            }

            var content = ReadNonEmptyString(delta, "content");
            var reasoning = delta.TryGetProperty("reasoning", out _)
                ? ReadNonEmptyString(delta, "reasoning")
                : ReadReasoningDetails(delta);
            return content is null && reasoning is null && usage is null
                ? null
                : new StreamDelta(content, reasoning, usage);
        }
        catch (JsonException exception)
        {
            ClientLog.MalformedPayload(logger, exception, payload.Length);
            return null;
        }
    }

    private static string? ReadNonEmptyString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var text = value.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? ReadReasoningDetails(JsonElement delta)
    {
        if (!delta.TryGetProperty("reasoning_details", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var result = new StringBuilder();
        foreach (var detail in details.EnumerateArray())
        {
            if (detail.ValueKind == JsonValueKind.Object)
            {
                result.Append(ReadNonEmptyString(detail, "text"));
            }
        }
        return result.Length == 0 ? null : result.ToString();
    }

    private static MessageUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object
            || !usage.TryGetProperty("prompt_tokens", out var prompt)
            || !usage.TryGetProperty("completion_tokens", out var completion)
            || prompt.ValueKind != JsonValueKind.Number
            || completion.ValueKind != JsonValueKind.Number
            || !prompt.TryGetInt32(out var promptTokens)
            || !completion.TryGetInt32(out var completionTokens))
        {
            return null;
        }

        decimal? cost = null;
        if (usage.TryGetProperty("total_cost", out var totalCost)
            && totalCost.ValueKind == JsonValueKind.Number
            && totalCost.TryGetDecimal(out var parsedTotal))
        {
            cost = parsedTotal;
        }
        else if (usage.TryGetProperty("cost", out var costElement)
            && costElement.ValueKind == JsonValueKind.Number
            && costElement.TryGetDecimal(out var parsedCost))
        {
            cost = parsedCost;
        }
        return new MessageUsage(promptTokens, completionTokens, cost);
    }
}
