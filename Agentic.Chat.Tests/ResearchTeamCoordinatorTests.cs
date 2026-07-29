using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Chat.Models.MultiAgent;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agentic.Chat.Tests;

public sealed class ResearchTeamCoordinatorTests
{
    private sealed class TestSearchProvider : ISearchProvider
    {
        private readonly List<SearchResultItem> _results;
        public TestSearchProvider(List<SearchResultItem> results) => _results = results;
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(_results);
    }

    /// <summary>LLM stub that returns a JSON Challenge object, exercising the bounce-back branch.</summary>
    private sealed class ChallengeLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
                return Task.FromResult(
                    "{\"target\":\"side effects\",\"question\":\"deeper evidence needed\",\"role\":\"📚\",\"hint\":\"side effects research papers\"}");
            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>Search provider that returns data only for the first few queries, then empty.</summary>
    private sealed class ThrottledSearchProvider : ISearchProvider
    {
        private int _callCount;
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount > 4) return Task.FromResult(new List<SearchResultItem>());
            return Task.FromResult(new List<SearchResultItem>
            {
                new SearchResultItem($"R{_callCount}", "snippet", $"https://x.com/{_callCount}", "Test")
            });
        }
    }

    /// <summary>Search provider that throws on every call (exercises exception handling).</summary>
    private sealed class AlwaysFailingProvider : ISearchProvider
    {
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Network down");
    }

    private sealed class TestLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => Task.FromResult($"Analysis for: {userPrompt}");
    }

    [Fact]
    public async Task ExecuteFullSessionAsync_EmitsAll20AgentRoles()
    {
        var items = new List<SearchResultItem>
        {
            new SearchResultItem("Test Article", "Test snippet", "https://example.org", "TestEngine")
        };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new TestLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Testing", Guid.NewGuid().ToString());

        Assert.NotEmpty(result.Frames);
        var allAgents = result.Frames.Select(f => f.SenderAgent).Distinct().ToList();

        Assert.Contains(allAgents, a => a.Contains("Research Director"));
        Assert.Contains(allAgents, a => a.Contains("Skeptic"));
        Assert.Contains(allAgents, a => a.Contains("Fact Verifier"));
        Assert.Contains(allAgents, a => a.Contains("Source Credibility"));
        Assert.Contains(allAgents, a => a.Contains("Recency"));
        Assert.Contains(allAgents, a => a.Contains("Myth"));
        Assert.Contains(allAgents, a => a.Contains("Synthesizer"));
        Assert.Contains(allAgents, a => a.Contains("Comparison"));
        Assert.Contains(allAgents, a => a.Contains("Simplifier"));
        Assert.Contains(allAgents, a => a.Contains("Citation"));
        Assert.Contains(allAgents, a => a.Contains("Host"));
    }

    [Fact]
    public async Task FullSession_ReturnsSearchItemsAndBuildsDashboard()
    {
        var items = new List<SearchResultItem>
        {
            new SearchResultItem("Paper A", "Abstract A", "https://arxiv.org/abs/1234", "ArXiv")
        };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new TestLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Quantum Computing", Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Frames);
        Assert.NotEmpty(result.SearchItems);

        var dashboard = coordinator.BuildDashboard("Quantum Computing", result.Frames.ToList(), result.SearchItems.ToList());
        Assert.NotNull(dashboard);
        Assert.NotEmpty(dashboard.Claims);
        Assert.All(dashboard.Claims, c => Assert.Equal(ClaimVerificationStatus.Unresolved, c.Status));
        Assert.NotEmpty(dashboard.UnresolvedQuestions);
        Assert.Equal(3, dashboard.RoundsCompleted);
    }

    [Fact]
    public async Task FullSession_MarksClaimsUnresolved_WhenNoSearchResults()
    {
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider([]),
            new TestLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Obscure Topic", Guid.NewGuid().ToString());

        Assert.NotEmpty(result.Frames);
        Assert.Empty(result.SearchItems);

        var dashboard = coordinator.BuildDashboard("Obscure Topic", result.Frames.ToList(), result.SearchItems.ToList());
        Assert.Single(dashboard.Claims);
        Assert.Equal(ClaimVerificationStatus.Unresolved, dashboard.Claims[0].Status);
        Assert.NotEmpty(dashboard.UnresolvedQuestions);
    }
    [Fact]
    public async Task ExecuteResearchSession_EnqueueChallenges_WhenSkepticFindsGaps()
    {
        var items = new List<SearchResultItem>
        {
            new SearchResultItem("Limited Source", "Only this article found", "https://example.org", "Test")
        };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new ChallengeLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var frames = new List<AgentActivityFrame>();
        await foreach (var frame in coordinator.ExecuteResearchSessionAsync("Topic X", Guid.NewGuid().ToString(), []))
        {
            frames.Add(frame);
        }

        Assert.NotEmpty(frames);
        Assert.Contains(frames, f => f.ActionKind == "CounterClaimChallenge");
        Assert.Contains(frames, f => f.ActionKind == "FactCheckAudit");
        Assert.Contains(frames, f => f.ActionKind == "MythBusting");
        Assert.Contains(frames, f => f.ActionKind == "FinalResponseRendered");
        Assert.Contains(frames, f => f.ActionKind == "TargetedReSearch"
            && f.SenderAgent == "📚 Academic & Literature");
    }

    [Fact]
    public async Task FullSession_BounceBackTriggered_WhenVerifierChallenges()
    {
        var items = new List<SearchResultItem>
        {
            new SearchResultItem("Seed", "Initial result", "https://example.org", "Test")
        };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new ChallengeLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic X", Guid.NewGuid().ToString());
        var reSearches = result.Frames.Where(f => f.ActionKind == "TargetedReSearch").ToList();
        Assert.NotEmpty(reSearches);
    }

    [Fact]
    public async Task FullSession_AllAgentFramesUseTruthfulStatus()
    {
        var items = new List<SearchResultItem>
        {
            new SearchResultItem("Article", "Snippet", "https://example.org", "Test")
        };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new TestLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Testing", Guid.NewGuid().ToString());

        var searchFrames = result.Frames.Where(f => f.ActionKind == "SearchResultRetrieved").ToList();
        foreach (var f in searchFrames)
        {
            Assert.Equal(ClaimVerificationStatus.Unresolved, f.StatusBadge);
        }

        var dash = coordinator.BuildDashboard("Testing", result.Frames.ToList(), result.SearchItems.ToList());
        Assert.All(dash.Claims, c => Assert.NotEqual(ClaimVerificationStatus.Verified, c.Status));
        Assert.All(dash.Citations, c => Assert.NotEqual(ClaimVerificationStatus.Verified, c.TrustRating));
        Assert.All(dash.Faqs, f => Assert.NotEmpty(f.Question));
        Assert.All(dash.Faqs, f => Assert.NotEmpty(f.Answer));
        Assert.All(dash.Timeline, t => Assert.NotEmpty(t.PeriodOrDate));
        Assert.All(dash.Timeline, t => Assert.NotEmpty(t.Title));
        Assert.All(dash.Timeline, t => Assert.NotEmpty(t.Description));
    }

    [Fact]
    public async Task FullSession_ExhaustsBudgetThenStops()
    {
        var coordinator = new ResearchTeamCoordinator(
            new ThrottledSearchProvider(),
            new ChallengeLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Frames);
    }

    [Fact]
    public async Task FullSession_HandlesAlwaysFailingProvider()
    {
        var coordinator = new ResearchTeamCoordinator(
            new AlwaysFailingProvider(),
            new TestLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        // SearchAsync inside the coordinator's gather loop catches and yields a frame; should not throw
        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Frames);
        var searchFrames = result.Frames.Where(f => f.ActionKind == "SearchResultRetrieved").ToList();
        Assert.NotEmpty(searchFrames);
    }

    [Fact]
    public async Task FullSession_DomainHelperFallsBackForBadUrl()
    {
        var items = new List<SearchResultItem> { new SearchResultItem("X", "y", "not a valid url", "S") };
        var coordinator = new ResearchTeamCoordinator(new TestSearchProvider(items), new TestLlmClient(), NullLogger<ResearchTeamCoordinator>.Instance);
        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        var dash = coordinator.BuildDashboard("Topic", result.Frames.ToList(), result.SearchItems.ToList());
        Assert.Contains(dash.Citations, c => c.Domain == "open-source");
    }

    [Fact]
    public async Task FullSession_AwaitsCallbackForEachFrame()
    {
        var items = new List<SearchResultItem> { new SearchResultItem("A", "B", "https://x", "S") };
        var coordinator = new ResearchTeamCoordinator(new TestSearchProvider(items), new TestLlmClient(), NullLogger<ResearchTeamCoordinator>.Instance);

        var callbackCount = 0;
        var result = await coordinator.ExecuteFullSessionAsync(
            "Topic",
            Guid.NewGuid().ToString(),
            onFrameProduced: f => { callbackCount++; return Task.CompletedTask; });

        Assert.NotEmpty(result.Frames);
        Assert.Equal(result.Frames.Count, callbackCount);
    }

    [Fact]
    public async Task FullSession_HostSummaryIsTruthful()
    {
        var items = new List<SearchResultItem>
        {
            new SearchResultItem("Article", "Snippet", "https://example.org", "Test")
        };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new TestLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Testing", Guid.NewGuid().ToString());
        var host = result.Frames.FirstOrDefault(f => f.ActionKind == "FinalResponseRendered");
        Assert.NotNull(host);
        Assert.Contains("Unresolved", host!.Payload);
        Assert.Contains("20-agent", host.ProgressSummary);
    }

    // ── Coverage edge cases ─────────────────────────────────────

    [Fact]
    public async Task ExecuteResearchSession_EmptyTopic_YieldsNoFrames()
    {
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider([]),
            new TestLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var frames = new List<AgentActivityFrame>();
        await foreach (var frame in coordinator.ExecuteResearchSessionAsync("", Guid.NewGuid().ToString(), []))
        {
            frames.Add(frame);
        }

        Assert.Empty(frames);
    }

    [Fact]
    public async Task FullSession_NoChallenge_CompletesNormally()
    {
        var items = new List<SearchResultItem> { new SearchResultItem("A", "B", "https://example.org", "Test") };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new NoChallengeLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Frames);
        Assert.DoesNotContain(result.Frames, f => f.ActionKind == "TargetedReSearch");
    }

    [Fact]
    public async Task FullSession_InvalidJsonChallenge_HandledGracefully()
    {
        var items = new List<SearchResultItem> { new SearchResultItem("A", "B", "https://example.org", "Test") };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new InvalidJsonLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Frames);
        Assert.DoesNotContain(result.Frames, f => f.ActionKind == "TargetedReSearch");
    }

    [Fact]
    public async Task FullSession_UnknownChallengeRole_FallsBackToGeneral()
    {
        var items = new List<SearchResultItem> { new SearchResultItem("A", "B", "https://example.org", "Test") };
        var coordinator = new ResearchTeamCoordinator(
            new TestSearchProvider(items),
            new UnknownRoleChallengeLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        var reSearches = result.Frames.Where(f => f.ActionKind == "TargetedReSearch").ToList();
        Assert.NotEmpty(reSearches);
        Assert.Contains(reSearches, f => f.SenderAgent == "\U0001f310 General Web Search");
    }

    [Fact]
    public async Task FullSession_BounceBackSearchError_HandlesGracefully()
    {
        var coordinator = new ResearchTeamCoordinator(
            new BounceBackFailingProvider(),
            new ChallengeLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        var reSearchErrors = result.Frames.Where(f => f.ActionKind == "TargetedReSearch" && f.Payload == "Search error.").ToList();
        Assert.NotEmpty(reSearchErrors);
    }

    [Fact]
    public async Task FullSession_ChallengeWithoutHint_FallsBackToTargetClaim()
    {
        var recorder = new RecordingSearchProvider(new SearchResultItem("A", "B", "https://example.org", "Test"));
        var coordinator = new ResearchTeamCoordinator(
            recorder,
            new NoHintChallengeLlmClient(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        // Bounce-back should use TargetClaim ("side effects") because SearchQueryHint is null
        var bounceBackQueries = recorder.Queries.Skip(6).ToList();
        Assert.NotEmpty(bounceBackQueries);
        Assert.All(bounceBackQueries, q => Assert.Equal("side effects", q));
    }

    /// <summary>LLM stub that returns "No further challenges." for verifiers, never raises challenges.</summary>
    private sealed class NoChallengeLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
                return Task.FromResult("No further challenges.");
            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>LLM stub that returns invalid JSON to exercise the TryParseChallenge catch block.</summary>
    private sealed class InvalidJsonLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
                return Task.FromResult("{invalid}");
            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>LLM stub that returns a challenge with no matching role (exercises the General fallback).</summary>
    private sealed class UnknownRoleChallengeLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
                return Task.FromResult("{\"target\":\"miss\",\"question\":\"why\",\"role\":\"❓UnknownRole\",\"hint\":\"search hint\"}");
            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>LLM stub that returns a challenge JSON without the "hint" field.</summary>
    private sealed class NoHintChallengeLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
                return Task.FromResult("{\"target\":\"side effects\",\"question\":\"deeper evidence needed\",\"role\":\"📚\"}");
            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>Succeeds for first 6 calls (initial gather phase), then throws on bounce-back.</summary>
    private sealed class BounceBackFailingProvider : ISearchProvider
    {
        private int _callCount;
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount <= 6)
                return Task.FromResult(new List<SearchResultItem> { new SearchResultItem("Result", "Snippet", "https://example.org", "Test") });
            throw new InvalidOperationException("Bounce-back network failure");
        }
    }

    /// <summary>Records every query passed to SearchAsync for later inspection.</summary>
    private sealed class RecordingSearchProvider : ISearchProvider
    {
        private readonly SearchResultItem _result;
        private readonly List<string> _queries = new();
        public IReadOnlyList<string> Queries => _queries;
        public RecordingSearchProvider(SearchResultItem result) => _result = result;
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            _queries.Add(query);
            return Task.FromResult(new List<SearchResultItem> { _result });
        }
    }
}
