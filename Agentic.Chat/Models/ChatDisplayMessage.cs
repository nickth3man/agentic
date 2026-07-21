namespace Agentic.Chat.Models;

public sealed class ChatDisplayMessage
{
    public required string Role { get; init; }

    public string Content { get; set; } = string.Empty;

    public string Reasoning { get; set; } = string.Empty;

    public bool IsStreaming { get; set; }
}
