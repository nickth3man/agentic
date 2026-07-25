using System.Text.Json.Serialization;

namespace Agentic.Chat.Models;

// The chat/completions request body. Reasoning / temperature / max_tokens are
// omitted when null (WhenWritingNull) so strict providers never see unsupported keys.
public sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("reasoning")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ReasoningRequest? Reasoning,
    [property: JsonPropertyName("temperature")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Temperature = null,
    [property: JsonPropertyName("max_tokens")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaxTokens = null);

// OpenRouter reasoning block: effort low/medium/high; omit the whole key when off.
public sealed record ReasoningRequest(
    [property: JsonPropertyName("effort")] string Effort,
    [property: JsonPropertyName("exclude")] bool Exclude);
