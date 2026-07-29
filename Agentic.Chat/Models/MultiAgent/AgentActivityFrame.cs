using System;

namespace Agentic.Chat.Models.MultiAgent;

public record AgentActivityFrame
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string SessionId { get; init; } = string.Empty;
    public int StepIndex { get; init; }
    public string SenderAgent { get; init; } = string.Empty;
    public string RecipientAgent { get; init; } = string.Empty;
    public string DivisionName { get; init; } = string.Empty;
    public string ActionKind { get; init; } = string.Empty;
    public string ProgressSummary { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public ClaimVerificationStatus? StatusBadge { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool IsExpanded { get; set; }
}
