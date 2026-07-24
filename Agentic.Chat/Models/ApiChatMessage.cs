using System.Text.Json.Serialization;

namespace Agentic.Chat.Models;

// Typed API-transcript message. Replaces the anonymous objects ChatAgentService
// previously stored in _apiMessages. Serialization MUST stay byte-identical to the
// prior anonymous shapes:
//   system/user : {"role":"...","content":"..."}
//   assistant   : {"role":"assistant","content":"..."} (no reasoning) or
//                 {"role":"assistant","content":"...","reasoning":"..."} (with reasoning)
// Reasoning is nullable + WhenWritingNull so it is omitted exactly when absent (matching
// the old conditional add). Property order is role, content, reasoning (declaration order).
public sealed record ApiChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("reasoning")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Reasoning);
