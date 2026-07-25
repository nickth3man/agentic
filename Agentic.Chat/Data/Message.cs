namespace Agentic.Chat.Data;

public sealed class Message
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Reasoning { get; set; }

    public string? ImageDataUrl { get; set; }

    public int? UsagePromptTokens { get; set; }

    public int? UsageCompletionTokens { get; set; }

    public decimal? UsageCost { get; set; }

    public bool UsageIsFree { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
