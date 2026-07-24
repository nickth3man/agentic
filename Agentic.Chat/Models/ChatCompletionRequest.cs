using System.Text.Json.Serialization;

namespace Agentic.Chat.Models;

// The chat/completions request body. Property order (model, messages, stream, reasoning)
// matches the key order ChatAgentService previously built with a Dictionary, so the
// serialized body is byte-identical. Reasoning is omitted entirely when the selected
// model does not support reasoning (WhenWritingNull).
public sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("reasoning")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ReasoningRequest? Reasoning);

// Matches the prior `new { enabled = true, exclude = false }` shape.
public sealed record ReasoningRequest(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("exclude")] bool Exclude);
