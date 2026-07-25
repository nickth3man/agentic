namespace Agentic.Chat.Models;

/// <summary>
/// Token and cost accounting for one assistant turn, parsed from the final OpenRouter
/// SSE usage chunk (or estimated from catalog pricing when cost is omitted).
/// </summary>
public sealed record MessageUsage(
    int PromptTokens,
    int CompletionTokens,
    decimal? Cost,
    bool IsFree = false);
