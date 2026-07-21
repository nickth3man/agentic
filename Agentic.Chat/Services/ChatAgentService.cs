using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Models;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Services;

public sealed class ChatAgentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<ChatAgentService> _logger;
    private readonly List<ChatDisplayMessage> _displayMessages = [];
    private readonly List<object> _apiMessages =
    [
        new { role = "system", content = "You are a helpful chat agent." }
    ];

    public ChatAgentService(
        IHttpClientFactory httpClientFactory,
        IOptions<OpenRouterOptions> options,
        ILogger<ChatAgentService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OpenRouter");
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<ChatDisplayMessage> Messages => _displayMessages;

    public async IAsyncEnumerable<ChatDisplayMessage> SendStreamingAsync(
        string userText,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);

        var trimmed = userText.Trim();
        _displayMessages.Add(new ChatDisplayMessage { Role = "user", Content = trimmed });
        _apiMessages.Add(new { role = "user", content = trimmed });

        var assistant = new ChatDisplayMessage
        {
            Role = "assistant",
            IsStreaming = true
        };
        _displayMessages.Add(assistant);
        yield return assistant;

        _logger.LogInformation(
            "Streaming chat completion with {MessageCount} message(s) in transcript",
            _apiMessages.Count);

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = _apiMessages,
            ["stream"] = true,
            ["reasoning"] = new { enabled = true, exclude = false }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            assistant.IsStreaming = false;
            assistant.Content = $"(Error {(int)response.StatusCode}: {Truncate(errorBody, 300)})";
            yield return assistant;
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
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

            if (!TryApplyDelta(payload, assistant))
            {
                continue;
            }

            yield return assistant;
        }

        assistant.IsStreaming = false;

        if (string.IsNullOrWhiteSpace(assistant.Content) && string.IsNullOrWhiteSpace(assistant.Reasoning))
        {
            assistant.Content = "(No response content returned.)";
        }

        // Keep assistant content (+ reasoning when present) in the API transcript for multi-turn continuity.
        if (!string.IsNullOrWhiteSpace(assistant.Reasoning))
        {
            _apiMessages.Add(new
            {
                role = "assistant",
                content = assistant.Content,
                reasoning = assistant.Reasoning
            });
        }
        else
        {
            _apiMessages.Add(new { role = "assistant", content = assistant.Content });
        }

        yield return assistant;
    }

    private static bool TryApplyDelta(string payload, ChatDisplayMessage assistant)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
            {
                return false;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
            {
                return false;
            }

            var changed = false;

            if (delta.TryGetProperty("reasoning", out var reasoningEl) &&
                reasoningEl.ValueKind == JsonValueKind.String)
            {
                var piece = reasoningEl.GetString();
                if (!string.IsNullOrEmpty(piece))
                {
                    assistant.Reasoning += piece;
                    changed = true;
                }
            }
            else if (delta.TryGetProperty("reasoning_details", out var details) &&
                     details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == JsonValueKind.String)
                    {
                        var piece = textEl.GetString();
                        if (!string.IsNullOrEmpty(piece))
                        {
                            assistant.Reasoning += piece;
                            changed = true;
                        }
                    }
                }
            }

            if (delta.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                var piece = contentEl.GetString();
                if (!string.IsNullOrEmpty(piece))
                {
                    assistant.Content += piece;
                    changed = true;
                }
            }

            return changed;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
