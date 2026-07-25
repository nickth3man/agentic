using Agentic.Chat.Models;

namespace Agentic.Chat.Services;

/// <summary>
/// Produces the model-visible slice of a chat transcript without changing the
/// complete, display-visible transcript owned by <see cref="ChatAgentService"/>.
/// </summary>
internal static class ContextWindow
{
    internal const int MinimumRecentMessages = 2;
    private const long BudgetPercent = 80;

    /// <summary>
    /// Estimates tokens using the deliberately inexpensive four-characters-per-token heuristic.
    /// Assistant reasoning is excluded because historical reasoning is never sent back to a model.
    /// </summary>
    internal static int EstimateTokens(IEnumerable<ApiChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages.Sum(message => (message.Content.Length + 3) / 4);
    }

    /// <summary>
    /// Keeps every system message and the most recent messages while dropping oldest
    /// user/assistant pairs until the request fits 80% of the model context window.
    /// Reasoning remains in the display transcript but is stripped from every historical
    /// assistant message in the returned API-only snapshot.
    /// </summary>
    internal static ContextWindowResult TrimToBudget(
        IReadOnlyList<ApiChatMessage> messages,
        long contextLength)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var sanitized = messages
            .Select(message => message.Role == "assistant"
                ? message with { Reasoning = null }
                : message)
            .ToList();
        var keep = Enumerable.Repeat(true, sanitized.Count).ToArray();
        var budget = contextLength > 0
            ? Math.Max(1L, contextLength * BudgetPercent / 100)
            : long.MaxValue;
        var keptNonSystemCount = sanitized.Count(message => message.Role != "system");

        while (EstimateTokens(KeptMessages(sanitized, keep)) > budget
            && keptNonSystemCount > MinimumRecentMessages)
        {
            var first = Enumerable.Range(0, keep.Length)
                .First(index => keep[index] && sanitized[index].Role != "system");
            keep[first] = false;
            keptNonSystemCount--;

            var second = Enumerable.Range(first + 1, keep.Length - first - 1)
                .Where(index => sanitized[index].Role != "system")
                .First();
            if (sanitized[first].Role == "user" && sanitized[second].Role == "assistant")
            {
                keep[second] = false;
                keptNonSystemCount--;
            }
        }

        var kept = KeptMessages(sanitized, keep);
        return new ContextWindowResult(
            kept,
            EstimateTokens(sanitized),
            sanitized.Count - kept.Count);
    }

    private static List<ApiChatMessage> KeptMessages(
        IReadOnlyList<ApiChatMessage> messages,
        bool[] keep)
        => messages.Where((_, index) => keep[index]).ToList();
}

internal sealed record ContextWindowResult(
    IReadOnlyList<ApiChatMessage> Messages,
    int TranscriptTokens,
    int ExcludedMessageCount);
