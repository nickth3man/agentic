namespace Agentic.Chat.Models;

public sealed class ChatDisplayMessage
{
    public required string Role { get; init; }

    public string Content { get; set; } = string.Empty;

    public string Reasoning { get; set; } = string.Empty;

    public bool IsStreaming { get; set; }

    // True when this assistant turn ended in an error (an OpenRouterException was
    // surfaced via the streaming core). Distinct from a successful assistant turn
    // so the UI can render an error affordance (retry) instead of treating the
    // error text as model-visible content. Error placeholders are NEVER appended
    // to the API transcript.
    public bool IsError { get; set; }
}
