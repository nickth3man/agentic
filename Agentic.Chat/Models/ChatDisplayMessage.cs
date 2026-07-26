namespace Agentic.Chat.Models;

public sealed class ChatDisplayMessage
{
    public required string Role { get; init; }

    public string Content { get; set; } = string.Empty;

    public string Reasoning { get; set; } = string.Empty;

    public bool IsStreaming { get; set; }

    /// <summary>
    /// Whether the user has explicitly expanded or collapsed this message's
    /// thinking panel. Once set, automatic panel state changes no longer apply.
    /// </summary>
    public bool ThinkingUserTouched { get; private set; }

    /// <summary>
    /// The explicit user-selected thinking-panel state. This is meaningful only
    /// after <see cref="ThinkingUserTouched"/> is true.
    /// </summary>
    public bool UserSelectedThinkingOpen { get; private set; }

    /// <summary>
    /// The moment the first reasoning token arrived.
    /// </summary>
    public DateTimeOffset? ReasoningStartedAt { get; private set; }

    /// <summary>
    /// The moment the first answer-content token arrived.
    /// </summary>
    public DateTimeOffset? ContentStartedAt { get; private set; }

    /// <summary>
    /// The time the stream completed, used when a reasoning-only response has
    /// no content token from which to calculate its thinking duration.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// True while the assistant is still in the reasoning phase (streaming and
    /// no answer content yet). Drives the Thinking pulse / shimmer — not panel open.
    /// </summary>
    public bool IsThinking => IsStreaming && string.IsNullOrEmpty(Content);

    public bool IsThinkingOpen
        => ThinkingUserTouched
            ? UserSelectedThinkingOpen
            : IsThinking;

    public int? ThoughtDurationSeconds
    {
        get
        {
            if (ReasoningStartedAt is not { } reasoningStartedAt
                || (ContentStartedAt ?? CompletedAt) is not { } thoughtEndedAt)
            {
                return null;
            }

            return Math.Max(1, (int)Math.Ceiling((thoughtEndedAt - reasoningStartedAt).TotalSeconds));
        }
    }

    public void SetThinkingOpenByUser(bool isOpen)
    {
        ThinkingUserTouched = true;
        UserSelectedThinkingOpen = isOpen;
    }

    /// <summary>
    /// Applies a stream delta and records the first reasoning/content milestones.
    /// The first content token automatically collapses thinking unless the user
    /// already selected a panel state.
    /// </summary>
    public bool ApplyDelta(StreamDelta delta, DateTimeOffset timestamp)
    {
        var changed = false;

        if (!string.IsNullOrEmpty(delta.Reasoning))
        {
            Reasoning += delta.Reasoning;
            ReasoningStartedAt ??= timestamp;
            changed = true;
        }

        if (!string.IsNullOrEmpty(delta.Content))
        {
            Content += delta.Content;
            ContentStartedAt ??= timestamp;
            changed = true;
        }

        if (delta.Usage is not null)
        {
            Usage = delta.Usage;
            changed = true;
        }

        return changed;
    }

    public void MarkCompleted(DateTimeOffset timestamp)
    {
        IsStreaming = false;
        CompletedAt ??= timestamp;
    }

    // True when this assistant turn ended in an error (an OpenRouterException was
    // surfaced via the streaming core). Distinct from a successful assistant turn
    // so the UI can render an error affordance (retry) instead of treating the
    // error text as model-visible content. Error placeholders are NEVER appended
    // to the API transcript.
    public bool IsError { get; set; }

    /// <summary>Data URL thumbnail for a user-sent image (display only).</summary>
    public string? ImageDataUrl { get; set; }

    /// <summary>
    /// Token/cost accounting from the final SSE usage chunk. Populated only after the
    /// stream completes — never while <see cref="IsStreaming"/> is true.
    /// </summary>
    public MessageUsage? Usage { get; set; }
}
