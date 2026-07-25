using System.Globalization;
using Agentic.Chat.Models;

namespace Agentic.Chat.Services;

internal static class UsageFormatter
{
    public static string FormatMessageFooter(MessageUsage usage)
    {
        var tokens = $"{FormatTokenCount(usage.PromptTokens)} in · {FormatTokenCount(usage.CompletionTokens)} out";
        if (usage.IsFree)
        {
            return $"{tokens} · free";
        }

        if (usage.Cost is decimal cost)
        {
            return $"{tokens} · {FormatCost(cost)}";
        }

        return tokens;
    }

    public static string FormatConversationTotal(IEnumerable<ChatDisplayMessage> messages)
    {
        var usages = messages
            .Where(m => m.Role == "assistant" && m.Usage is not null)
            .Select(m => m.Usage!)
            .ToList();

        if (usages.Count == 0)
        {
            return string.Empty;
        }

        if (usages.All(u => u.IsFree))
        {
            return "Session · free";
        }

        var totalCost = usages.Sum(u => u.Cost ?? 0m);
        return totalCost > 0m
            ? $"Session · {FormatCost(totalCost)}"
            : "Session · free";
    }

    internal static string FormatTokenCount(int tokens)
        => tokens >= 10_000
            ? $"{(tokens / 1000m).ToString("0.#", CultureInfo.InvariantCulture)}k"
            : tokens >= 1_000
                ? $"{(tokens / 1000m).ToString("0.0", CultureInfo.InvariantCulture)}k"
                : tokens.ToString(CultureInfo.InvariantCulture);

    internal static string FormatCost(decimal cost)
        => cost >= 0.01m
            ? $"${cost.ToString("0.00", CultureInfo.InvariantCulture)}"
            : $"${cost.ToString("0.0000", CultureInfo.InvariantCulture)}";
}
