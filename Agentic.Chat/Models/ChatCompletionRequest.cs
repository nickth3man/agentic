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
    ReasoningRequest? Reasoning,
    [property: JsonPropertyName("usage")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    UsageRequest? Usage);

// Matches the prior `new { enabled = true, exclude = false }` shape.
public sealed record ReasoningRequest(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("exclude")] bool Exclude);

// OpenRouter usage accounting — emits token/cost on the final SSE chunk when included.
public sealed record UsageRequest(
    [property: JsonPropertyName("include")] bool Include);
