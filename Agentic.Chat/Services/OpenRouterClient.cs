using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Models;

namespace Agentic.Chat.Services;

public sealed class OpenRouterClient : IOpenRouterClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;

    public OpenRouterClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("OpenRouter");
    }

    public async IAsyncEnumerable<StreamDelta> StreamChatAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new OpenRouterException((int)response.StatusCode, errorBody);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data:".Length..].Trim();
            if (payload is "[DONE]")
            {
                break;
            }

            var delta = DecodeDelta(payload);
            if (delta is null)
            {
                continue;
            }

            yield return delta;
        }
    }

    // Decodes one SSE `data:` payload into a StreamDelta, or null when the payload carries
    // no applicable delta (invalid JSON, missing/empty choices, missing delta, or a delta
    // whose content/reasoning pieces are all empty). Migrated verbatim from the prior
    // ChatAgentService.TryApplyDelta logic, but stateless: it returns the decoded pieces
    // instead of mutating a ChatDisplayMessage.
    internal static StreamDelta? DecodeDelta(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
            {
                return null;
            }

            string? reasoning = null;

            if (delta.TryGetProperty("reasoning", out var reasoningEl) &&
                reasoningEl.ValueKind == JsonValueKind.String)
            {
                var piece = reasoningEl.GetString();
                if (!string.IsNullOrEmpty(piece))
                {
                    reasoning = piece;
                }
            }
            else if (delta.TryGetProperty("reasoning_details", out var details) &&
                     details.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == JsonValueKind.String)
                    {
                        var piece = textEl.GetString();
                        if (!string.IsNullOrEmpty(piece))
                        {
                            sb.Append(piece);
                        }
                    }
                }
                if (sb.Length > 0)
                {
                    reasoning = sb.ToString();
                }
            }

            string? content = null;
            if (delta.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                var piece = contentEl.GetString();
                if (!string.IsNullOrEmpty(piece))
                {
                    content = piece;
                }
            }

            if (reasoning is null && content is null)
            {
                var usageOnly = ParseUsage(doc.RootElement);
                if (usageOnly is null)
                {
                    return null;
                }

                return new StreamDelta(null, null, usageOnly);
            }

            var usage = ParseUsage(doc.RootElement);
            return new StreamDelta(content, reasoning, usage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static MessageUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageEl) ||
            usageEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!usageEl.TryGetProperty("prompt_tokens", out var promptEl) ||
            !usageEl.TryGetProperty("completion_tokens", out var completionEl) ||
            promptEl.ValueKind != JsonValueKind.Number ||
            completionEl.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        decimal? cost = null;
        if (usageEl.TryGetProperty("total_cost", out var totalCostEl) &&
            totalCostEl.ValueKind == JsonValueKind.Number)
        {
            cost = totalCostEl.GetDecimal();
        }
        else if (usageEl.TryGetProperty("cost", out var costEl) &&
                 costEl.ValueKind == JsonValueKind.Number)
        {
            cost = costEl.GetDecimal();
        }

        return new MessageUsage(promptEl.GetInt32(), completionEl.GetInt32(), cost);
    }
}
