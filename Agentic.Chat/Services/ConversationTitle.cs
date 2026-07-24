namespace Agentic.Chat.Services;

// Auto-title helper for new conversations (first ~40 chars of the first user message).
public static class ConversationTitle
{
    public const int MaxLength = 40;

    public const string Default = "New chat";

    public static string FromFirstUserMessage(string? firstUserMessage)
    {
        if (string.IsNullOrWhiteSpace(firstUserMessage))
        {
            return Default;
        }

        var collapsed = string.Join(
            ' ',
            firstUserMessage.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length <= MaxLength)
        {
            return collapsed;
        }

        return collapsed[..MaxLength].TrimEnd() + "…";
    }
}
