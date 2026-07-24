namespace Agentic.Chat.Data;

public sealed class Message
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Reasoning { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
