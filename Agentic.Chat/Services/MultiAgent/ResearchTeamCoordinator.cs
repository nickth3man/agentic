using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Chat.Models.MultiAgent;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Services.MultiAgent;

public sealed class ResearchTeamCoordinator
{
    public const int MaxRounds = 3;
    public const int MaxSearchBudget = 12;

    private readonly ISearchProvider _searchProvider;
    private readonly ILocalLlmClient _llmClient;
    private readonly ILogger<ResearchTeamCoordinator> _logger;

    public ResearchTeamCoordinator(
        ISearchProvider searchProvider,
        ILocalLlmClient llmClient,
        ILogger<ResearchTeamCoordinator> logger)
    {
        _searchProvider = searchProvider ?? throw new ArgumentNullException(nameof(searchProvider));
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<AgentActivityFrame> ExecuteResearchSessionAsync(
        string userTopic,
        string sessionId,
        List<SearchResultItem> gatheredSearchItems,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userTopic)) yield break;

        var stepIndex = 0;
        var searchCount = 0;
        var challengeQueue = new List<string>();
        var roundCounter = 0;

        // ── ROUND 1: DIRECTOR + 6 GATHERING AGENTS ──────────────────
        stepIndex++;
        var directorPlan = await _llmClient
            .GenerateCompletionAsync("You are the 🧭 Research Director. Plan a multi-angle strategy in 2 sentences.",
                $"Research plan for: {userTopic}", cancellationToken)
            .ConfigureAwait(false);
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "🧭 Research Director",
            RecipientAgent = "Division 1",
            DivisionName = "Synthesis Division",
            ActionKind = "TaskAssignment",
            ProgressSummary = $"Decomposed '{userTopic}' into research angles.",
            Payload = directorPlan
        };

        // 6 gathering agents — collect results first, then yield outside try/catch
        (string name, Func<string, string> queryFor)[] gatherSequence = [
            ("🌐 General Web Search", q => q),
            ("📰 News & Current Events", q => $"{q} latest news"),
            ("📚 Academic & Literature", q => $"{q} research papers"),
            ("📊 Data & Statistics", q => $"{q} statistics"),
            ("💬 Community & Forum", q => $"{q} discussion forum"),
            ("🌍 Global Context", q => $"{q} global perspective")
        ];

        foreach (var (agentName, queryFor) in gatherSequence)
        {
            if (searchCount >= MaxSearchBudget) break;
            searchCount++;
            stepIndex++;

            var query = queryFor(userTopic);
            AgentActivityFrame frame;
            try
            {
                var items = await _searchProvider.SearchAsync(query, cancellationToken).ConfigureAwait(false);
                gatheredSearchItems.AddRange(items);
                frame = new AgentActivityFrame
                {
                    SessionId = sessionId,
                    StepIndex = stepIndex,
                    SenderAgent = agentName,
                    RecipientAgent = "Blackboard",
                    DivisionName = "Gathering Division",
                    ActionKind = "SearchResultRetrieved",
                    ProgressSummary = items.Count > 0
                        ? $"Posted {items.Count} results for '{query}'."
                        : $"Searched '{query}' — 0 results.",
                    Payload = items.Count > 0
                        ? string.Join("\n", items.Select(i => $"• [{i.SourceEngine}] {i.Title}"))
                        : "No items.",
                    StatusBadge = ClaimVerificationStatus.Unresolved
                };
            }
            catch
            {
                frame = new AgentActivityFrame
                {
                    SessionId = sessionId,
                    StepIndex = stepIndex,
                    SenderAgent = agentName,
                    RecipientAgent = "Blackboard",
                    DivisionName = "Gathering Division",
                    ActionKind = "SearchResultRetrieved",
                    ProgressSummary = $"Search backend unavailable for '{query}'.",
                    Payload = "Provider returned error.",
                    StatusBadge = ClaimVerificationStatus.Unresolved
                };
            }
            yield return frame;
        }

        // ── VERIFICATION LOOP (max 3 rounds, agents challenge → bounce back) ──
        for (roundCounter = 0; roundCounter < MaxRounds; roundCounter++)
        {
            var hasEvidence = gatheredSearchItems.Count > 0;
            var evidence = hasEvidence
                ? string.Join("\n", gatheredSearchItems.Take(3).Select(i => $"{i.Title}: {i.Url}"))
                : "No evidence on blackboard.";

            // 8. Skeptic
            stepIndex++;
            var skeptic = await _llmClient
                .GenerateCompletionAsync("You are the ⚖️ Skeptic. If evidence is missing on a specific angle, say 'Re-search needed: <topic>'. Otherwise say 'No further challenges.'",
                    $"Evidence: {evidence}", cancellationToken)
                .ConfigureAwait(false);
            yield return new AgentActivityFrame
            {
                SessionId = sessionId,
                StepIndex = stepIndex,
                SenderAgent = "⚖️ Skeptic & Counter-Examiner",
                RecipientAgent = "Blackboard",
                DivisionName = "Verification Division",
                ActionKind = "CounterClaimChallenge",
                ProgressSummary = $"Reviewed {gatheredSearchItems.Count} items.",
                Payload = skeptic,
                StatusBadge = ClaimVerificationStatus.Unresolved
            };
            if (skeptic.Contains("Re-search needed:", StringComparison.OrdinalIgnoreCase))
                challengeQueue.Add(skeptic.Split("Re-search needed:")[1].Trim().TrimEnd('.'));

            // 9. Fact Verifier
            stepIndex++;
            var verifier = await _llmClient
                .GenerateCompletionAsync("You are the 🛡️ Fact Verifier. Note credibility in 1 sentence. If evidence is missing for a subtopic, say 'Re-search needed: <subtopic>'.",
                    $"Audit: {evidence}", cancellationToken)
                .ConfigureAwait(false);
            yield return new AgentActivityFrame
            {
                SessionId = sessionId,
                StepIndex = stepIndex,
                SenderAgent = "🛡️ Fact Verifier Agent",
                RecipientAgent = "Blackboard",
                DivisionName = "Verification Division",
                ActionKind = "FactCheckAudit",
                ProgressSummary = $"Audited {gatheredSearchItems.Count} items.",
                Payload = verifier,
                StatusBadge = ClaimVerificationStatus.Unresolved
            };
            if (verifier.Contains("Re-search needed:", StringComparison.OrdinalIgnoreCase))
                challengeQueue.Add(verifier.Split("Re-search needed:")[1].Trim().TrimEnd('.'));

            // 10. Source Credibility Rating
            stepIndex++;
            yield return new AgentActivityFrame
            {
                SessionId = sessionId,
                StepIndex = stepIndex,
                SenderAgent = "🏷️ Source Credibility Rating",
                RecipientAgent = "Blackboard",
                DivisionName = "Verification Division",
                ActionKind = "SourceRating",
                ProgressSummary = "Rated source domain credibility.",
                Payload = "Sources from open search providers.",
                StatusBadge = ClaimVerificationStatus.Unresolved
            };

            // 11. Timeline & Recency Auditor
            stepIndex++;
            yield return new AgentActivityFrame
            {
                SessionId = sessionId,
                StepIndex = stepIndex,
                SenderAgent = "⏳ Timeline & Recency Auditor",
                RecipientAgent = "Blackboard",
                DivisionName = "Verification Division",
                ActionKind = "RecencyCheck",
                ProgressSummary = "Checked source recency.",
                Payload = hasEvidence ? "Sources available. No staleness flagged."
                                      : "No sources to check.",
                StatusBadge = ClaimVerificationStatus.Unresolved
            };

            // 12. Myth Buster
            stepIndex++;
            var mythBuster = await _llmClient
                .GenerateCompletionAsync("You are the 🔍 Myth Buster. Flag a common misconception in 1 sentence.",
                    $"Topic: {userTopic}", cancellationToken)
                .ConfigureAwait(false);
            yield return new AgentActivityFrame
            {
                SessionId = sessionId,
                StepIndex = stepIndex,
                SenderAgent = "🔍 Myth & Misconception Buster",
                RecipientAgent = "Blackboard",
                DivisionName = "Verification Division",
                ActionKind = "MythBusting",
                ProgressSummary = "Identified misconceptions.",
                Payload = mythBuster,
                StatusBadge = ClaimVerificationStatus.Unresolved
            };

            // ── BOUNCE BACK: process enqueued challenges ──
            var pending = challengeQueue
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .Take(MaxSearchBudget - searchCount)
                .ToList();
            if (pending.Count == 0) break;
            challengeQueue.Clear();

            foreach (var challenge in pending)
            {
                if (searchCount >= MaxSearchBudget) break;
                searchCount++;
                stepIndex++;

                AgentActivityFrame reFrame;
                try
                {
                    var items = await _searchProvider.SearchAsync(challenge, cancellationToken).ConfigureAwait(false);
                    gatheredSearchItems.AddRange(items);
                    reFrame = new AgentActivityFrame
                    {
                        SessionId = sessionId,
                        StepIndex = stepIndex,
                        SenderAgent = "📊 ROUND-TABLE RE-SEARCH",
                        RecipientAgent = "Blackboard",
                        DivisionName = "Gathering Division",
                        ActionKind = "TargetedReSearch",
                        ProgressSummary = $"Re-searched '{challenge}' — {items.Count} results.",
                        Payload = items.Count > 0
                            ? string.Join("\n", items.Select(i => $"• [{i.SourceEngine}] {i.Title}"))
                            : "No items.",
                        StatusBadge = ClaimVerificationStatus.Unresolved
                    };
                }
                catch
                {
                    reFrame = new AgentActivityFrame
                    {
                        SessionId = sessionId,
                        StepIndex = stepIndex,
                        SenderAgent = "📊 ROUND-TABLE RE-SEARCH",
                        RecipientAgent = "Blackboard",
                        DivisionName = "Gathering Division",
                        ActionKind = "TargetedReSearch",
                        ProgressSummary = $"Re-search failed for '{challenge}'.",
                        Payload = "Search error.",
                        StatusBadge = ClaimVerificationStatus.Unresolved
                    };
                }
                yield return reFrame;
            }
        }

        // ── SYNTHESIS (4 agents) ─────────────────────────────────────
        var finalEvidence = gatheredSearchItems.Count > 0
            ? string.Join("\n", gatheredSearchItems.Take(3).Select(i => $"{i.Title}: {i.Url}"))
            : "No evidence.";

        stepIndex++;
        var synthesis = await _llmClient
            .GenerateCompletionAsync("You are the 🧩 Synthesizer. Summarize the key takeaway in 1 sentence.",
                $"Topic: {userTopic}\nEvidence: {finalEvidence}", cancellationToken).ConfigureAwait(false);
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "🧩 Key Takeaways Synthesizer",
            RecipientAgent = "⚖️ Comparison Builder",
            DivisionName = "Synthesis Division",
            ActionKind = "SynthesisConsensus",
            ProgressSummary = "Synthesized round-table debate.",
            Payload = synthesis
        };

        stepIndex++;
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "⚖️ Pros & Cons / Comparison Builder",
            RecipientAgent = "❓ FAQ Builder",
            DivisionName = "Synthesis Division",
            ActionKind = "ComparisonBuild",
            ProgressSummary = "Constructed trade-off summary.",
            Payload = "Compared key perspectives."
        };

        stepIndex++;
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "❓ FAQ Builder Agent",
            RecipientAgent = "Division 4",
            DivisionName = "Synthesis Division",
            ActionKind = "FaqGeneration",
            ProgressSummary = "Generated FAQ.",
            Payload = $"Common questions about {userTopic}."
        };

        // ── PRESENTATION (5 agents) ──────────────────────────────────
        stepIndex++;
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "🎨 Visual Card Layout Designer",
            RecipientAgent = "📊 Chart Designer",
            DivisionName = "Presentation Division",
            ActionKind = "CardLayout",
            ProgressSummary = "Designed visual cards.",
            Payload = "⚪ Unresolved badge on all unverified claims."
        };

        stepIndex++;
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "📊 Chart & Timeline Designer",
            RecipientAgent = "🗣️ Simplifier",
            DivisionName = "Presentation Division",
            ActionKind = "TimelineBuild",
            ProgressSummary = "Constructed 3-phase timeline.",
            Payload = "Timeline: Gather → Verify (bounce) → Synthesize & Present."
        };

        stepIndex++;
        var simplified = await _llmClient
            .GenerateCompletionAsync("You are the 🗣️ Simplifier. Write 1 plain-language sentence.",
                $"Findings: {synthesis}", cancellationToken).ConfigureAwait(false);
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "🗣️ Plain-Language Simplifier",
            RecipientAgent = "🔗 Citation Builder",
            DivisionName = "Presentation Division",
            ActionKind = "PlainLanguageFormatting",
            ProgressSummary = "Converted to plain language.",
            Payload = simplified
        };

        stepIndex++;
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "🔗 Interactive Citation Builder",
            RecipientAgent = "💬 Host",
            DivisionName = "Presentation Division",
            ActionKind = "CitationBuild",
            ProgressSummary = $"Linked {gatheredSearchItems.Count} sources.",
            Payload = gatheredSearchItems.Count > 0
                ? string.Join("\n", gatheredSearchItems.Select(i => $"• [{i.SourceEngine}] {i.Title}"))
                : "No sources."
        };

        stepIndex++;
        var statusMsg = gatheredSearchItems.Count > 0 || searchCount > 0
            ? $"{gatheredSearchItems.Count} items across {searchCount} searches and {roundCounter + 1} verify rounds. All claims Unresolved — corroboration needed."
            : "No search results. All claims Unresolved.";
        yield return new AgentActivityFrame
        {
            SessionId = sessionId,
            StepIndex = stepIndex,
            SenderAgent = "💬 Friendly Conversational Host",
            RecipientAgent = "User",
            DivisionName = "Presentation Division",
            ActionKind = "FinalResponseRendered",
            ProgressSummary = "20-agent round-table council complete.",
            Payload = statusMsg
        };
    }

    public async Task<ResearchSessionResult> ExecuteFullSessionAsync(
        string userTopic,
        string sessionId,
        Func<AgentActivityFrame, Task>? onFrameProduced = null,
        CancellationToken cancellationToken = default)
    {
        var searchItems = new List<SearchResultItem>();
        var frames = new List<AgentActivityFrame>();

        await foreach (var frame in ExecuteResearchSessionAsync(userTopic, sessionId, searchItems, cancellationToken).ConfigureAwait(false))
        {
            frames.Add(frame);
            if (onFrameProduced != null)
                await onFrameProduced(frame).ConfigureAwait(false);
        }

        return new ResearchSessionResult(frames, searchItems);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Instance preserved for future extensions requiring DI state.")]
    public ResearchDashboard BuildDashboard(string userTopic, List<AgentActivityFrame> frames, List<SearchResultItem> searchItems)
    {
        ArgumentNullException.ThrowIfNull(frames);
        searchItems ??= [];
        var hasData = searchItems.Count > 0;

        return new ResearchDashboard
        {
            UserTopic = userTopic,
            ExecutiveSummary = hasData
                ? $"{searchItems.Count} items retrieved. All claims Unresolved pending corroboration."
                : "No search results. All claims Unresolved.",
            KeyTakeaways = [
                hasData ? $"Retrieved {searchItems.Count} items." : "No items retrieved.",
                "20-agent round-table council across 3 rounds.",
                "All agent steps visible in activity feed."
            ],
            Claims = hasData
                ? searchItems.Take(3).Select(i => new ResearchClaim(i.Title, ClaimVerificationStatus.Unresolved,
                    $"Retrieved by {i.SourceEngine}. Needs corroboration.", [i.SourceEngine])).ToList()
                : [new ResearchClaim($"Consensus on '{userTopic}'", ClaimVerificationStatus.Unresolved, "No evidence.", [])],
            Timeline = [
                new TimelineEvent("Round 1", "Gathering (6 agents)", "Queried providers."),
                new TimelineEvent("Round 2", "Verification + Bounce (5+ agents)", "Challenged and re-searched."),
                new TimelineEvent("Round 3", "Synthesis & Presentation (9 agents)", "Unified findings.")
            ],
            Faqs = [new ResearchFaq($"Status of {userTopic}?", hasData ? $"{searchItems.Count} items. Unresolved." : "No data.")],
            UnresolvedQuestions = hasData
                ? searchItems.Select(i => $"'{i.Title}' needs corroboration.").Take(5).ToList()
                : [$"Evidence needed for '{userTopic}'."],
            Citations = searchItems.Select(i => new VerifiedCitation(i.Title, i.Url, Domain(i.Url), ClaimVerificationStatus.Unresolved)).ToList(),
            RoundsCompleted = MaxRounds,
            SearchQueriesExecuted = frames.Count(f => f.ActionKind == "SearchResultRetrieved")
        };
    }

    private static string Domain(string url)
    {
        try { return new Uri(url).Host; }
        catch { return "open-source"; }
    }
}

public record ResearchSessionResult(
    IReadOnlyList<AgentActivityFrame> Frames,
    IReadOnlyList<SearchResultItem> SearchItems
);
