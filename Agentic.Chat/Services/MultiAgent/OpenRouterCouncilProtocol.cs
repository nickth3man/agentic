using System.Text.Json;
using System.Text.Json.Serialization;
using Agentic.Chat.Models;

namespace Agentic.Chat.Services.MultiAgent;

internal static class OpenRouterCouncilProtocol
{
    internal const string Model = "qwen/qwen3.7-flash";
    internal const int MaxTokens = 320;

    private static readonly CouncilReasoningRequest Reasoning = new("none");

    internal static CouncilCompletionRequest CreateRequest(string systemPrompt, string userPrompt)
        => new(
            Model,
            [
                new ApiChatMessage("system", ApiChatMessageContent.FromText(systemPrompt), Reasoning: null),
                new ApiChatMessage("user", ApiChatMessageContent.FromText(userPrompt), Reasoning: null),
            ],
            Stream: false,
            MaxTokens,
            Reasoning);

    internal static async ValueTask<string?> ReadResponseTextAsync(
        Stream stream,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        var response = await JsonSerializer
            .DeserializeAsync<CouncilCompletionResponse>(stream, options, cancellationToken)
            .ConfigureAwait(false);
        return response?.FirstMessageText();
    }

    internal sealed record CouncilCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ApiChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("reasoning")] CouncilReasoningRequest Reasoning);

    internal sealed record CouncilReasoningRequest(
        [property: JsonPropertyName("effort")] string Effort);

    private sealed class CouncilCompletionResponse
    {
        [JsonPropertyName("choices")] public List<CouncilChoice>? Choices { get; init; }

        public string? FirstMessageText()
        {
            if (Choices is null || Choices.Count == 0) return null;
            return Choices[0].Message?.GetText();
        }
    }

    private sealed class CouncilChoice
    {
        [JsonPropertyName("message")] public CouncilMessage? Message { get; init; }
    }

    private sealed class CouncilMessage
    {
        [JsonPropertyName("content")] public JsonElement Content { get; init; }

        public string? GetText()
        {
            if (Content.ValueKind == JsonValueKind.String)
            {
                return Content.GetString();
            }

            if (Content.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return Content.Deserialize<ApiChatMessageContent>().GetDisplayText();
        }
    }
}
