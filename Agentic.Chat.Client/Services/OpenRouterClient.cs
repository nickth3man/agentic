using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Models;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Services;

public sealed class OpenRouterClient(
    OpenRouterCredentialService credentials,
    IOptions<OpenRouterOptions> options) : IOpenRouterClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenRouterCredentialService _credentials = credentials;
    private readonly OpenRouterOptions _options = options.Value;

    public async IAsyncEnumerable<StreamDelta> StreamChatAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var apiKey = await _credentials
            .GetKeyForModelAsync(request.Model)
            .ConfigureAwait(false);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
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

        using var response = await client
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

    private static StreamDelta? DecodeDelta(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : "OpenRouter stopped the stream.";
                var code = error.TryGetProperty("code", out var codeElement)
                    && codeElement.TryGetInt32(out var parsedCode)
                        ? parsedCode
                        : 500;
                throw new OpenRouterException(code, message ?? "OpenRouter stopped the stream.");
            }

            var usage = ParseUsage(root);
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("delta", out var delta))
            {
                return usage is null ? null : new StreamDelta(null, null, usage);
            }

            var content = ReadNonEmptyString(delta, "content");
            var reasoning = ReadNonEmptyString(delta, "reasoning")
                ?? ReadReasoningDetails(delta);
            return content is null && reasoning is null && usage is null
                ? null
                : new StreamDelta(content, reasoning, usage);
        }
        catch (JsonException)
        {
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
            result.Append(ReadNonEmptyString(detail, "text"));
        }
        return result.Length == 0 ? null : result.ToString();
    }

    private static MessageUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)
            || !usage.TryGetProperty("prompt_tokens", out var prompt)
            || !usage.TryGetProperty("completion_tokens", out var completion)
            || !prompt.TryGetInt32(out var promptTokens)
            || !completion.TryGetInt32(out var completionTokens))
        {
            return null;
        }

        decimal? cost = null;
        if (usage.TryGetProperty("total_cost", out var totalCost)
            && totalCost.TryGetDecimal(out var parsedTotal))
        {
            cost = parsedTotal;
        }
        else if (usage.TryGetProperty("cost", out var costElement)
            && costElement.TryGetDecimal(out var parsedCost))
        {
            cost = parsedCost;
        }
        return new MessageUsage(promptTokens, completionTokens, cost);
    }
}
