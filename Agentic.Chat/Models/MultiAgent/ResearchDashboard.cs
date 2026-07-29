using System;
using System.Collections.Generic;

namespace Agentic.Chat.Models.MultiAgent;

public record ResearchClaim(
    string ClaimText,
    ClaimVerificationStatus Status,
    string Explanation,
    List<string> Sources
);

public record TimelineEvent(
    string PeriodOrDate,
    string Title,
    string Description
);

public record ResearchFaq(
    string Question,
    string Answer
);

public record VerifiedCitation(
    string SourceTitle,
    string Url,
    string Domain,
    ClaimVerificationStatus TrustRating
);

public class ResearchDashboard
{
    public string UserTopic { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public List<string> KeyTakeaways { get; set; } = [];
    public List<ResearchClaim> Claims { get; set; } = [];
    public List<TimelineEvent> Timeline { get; set; } = [];
    public List<ResearchFaq> Faqs { get; set; } = [];
    public List<string> UnresolvedQuestions { get; set; } = [];
    public List<VerifiedCitation> Citations { get; set; } = [];
    public int RoundsCompleted { get; set; }
    public int SearchQueriesExecuted { get; set; }
}
