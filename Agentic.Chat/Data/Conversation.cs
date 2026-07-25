namespace Agentic.Chat.Data;

public sealed class Conversation
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "New chat";

    public string Model { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<Message> Messages { get; set; } = [];
}
