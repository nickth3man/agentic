namespace Agentic.Chat.Models;

// One decoded SSE chunk yielded by IOpenRouterClient. A null field means "no update for
// this field this chunk"; the consumer (ChatAgentService) only appends non-null pieces.
// DecodeDelta returns null entirely when the chunk carries no applicable delta.
public sealed record StreamDelta(string? Content, string? Reasoning);
