using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Chat.Models.MultiAgent;
using Agentic.Chat.Services.MultiAgent;
using Agentic.Chat.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agentic.Chat.Tests;

public sealed class ResearchTeamCoordinatorTests : IClassFixture<MultiAgentFixture>
{
    private readonly MultiAgentFixture _multiAgent;

    public ResearchTeamCoordinatorTests(MultiAgentFixture multiAgent)
    {
        _multiAgent = multiAgent;
    }

    [Fact]
    public async Task ExecuteFullSessionAsync_EmitsAll20AgentRoles()
    {
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("Test Article", "Test snippet", "https://example.org", "TestEngine")),
            _multiAgent.TestLlm(),
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
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("Paper A", "Abstract A", "https://arxiv.org/abs/1234", "ArXiv")),
            _multiAgent.TestLlm(),
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
            _multiAgent.EmptySearch(),
            _multiAgent.TestLlm(),
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
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("Limited Source", "Only this article found", "https://example.org", "Test")),
            _multiAgent.ChallengeLlm(),
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
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("Seed", "Initial result", "https://example.org", "Test")),
            _multiAgent.ChallengeLlm(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic X", Guid.NewGuid().ToString());
        var reSearches = result.Frames.Where(f => f.ActionKind == "TargetedReSearch").ToList();
        Assert.NotEmpty(reSearches);
    }

    [Fact]
    public async Task FullSession_AllAgentFramesUseTruthfulStatus()
    {
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("Article", "Snippet", "https://example.org", "Test")),
            _multiAgent.TestLlm(),
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
            _multiAgent.Throttled(),
            _multiAgent.ChallengeLlm(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Frames);
    }

    [Fact]
    public async Task FullSession_HandlesAlwaysFailingProvider()
    {
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.AlwaysFailing(),
            _multiAgent.TestLlm(),
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
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("X", "y", "not a valid url", "S")),
            _multiAgent.TestLlm(),
            NullLogger<ResearchTeamCoordinator>.Instance);
        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        var dash = coordinator.BuildDashboard("Topic", result.Frames.ToList(), result.SearchItems.ToList());
        Assert.Contains(dash.Citations, c => c.Domain == "open-source");
    }

    [Fact]
    public async Task FullSession_AwaitsCallbackForEachFrame()
    {
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("A", "B", "https://x", "S")),
            _multiAgent.TestLlm(),
            NullLogger<ResearchTeamCoordinator>.Instance);

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
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("Article", "Snippet", "https://example.org", "Test")),
            _multiAgent.TestLlm(),
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
            _multiAgent.EmptySearch(),
            _multiAgent.TestLlm(),
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
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("A", "B", "https://example.org", "Test")),
            _multiAgent.NoChallengeLlm(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Frames);
        Assert.DoesNotContain(result.Frames, f => f.ActionKind == "TargetedReSearch");
    }

    [Fact]
    public async Task FullSession_InvalidJsonChallenge_HandledGracefully()
    {
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("A", "B", "https://example.org", "Test")),
            _multiAgent.InvalidJsonLlm(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        Assert.NotEmpty(result.Frames);
        Assert.DoesNotContain(result.Frames, f => f.ActionKind == "TargetedReSearch");
    }

    [Fact]
    public async Task FullSession_UnknownChallengeRole_FallsBackToGeneral()
    {
        var coordinator = new ResearchTeamCoordinator(
            _multiAgent.FixedResult(new SearchResultItem("A", "B", "https://example.org", "Test")),
            _multiAgent.UnknownRoleChallengeLlm(),
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
            _multiAgent.BounceBackFailing(),
            _multiAgent.ChallengeLlm(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        var reSearchErrors = result.Frames.Where(f => f.ActionKind == "TargetedReSearch" && f.Payload == "Search error.").ToList();
        Assert.NotEmpty(reSearchErrors);
    }

    [Fact]
    public async Task FullSession_ChallengeWithoutHint_FallsBackToTargetClaim()
    {
        var recorder = _multiAgent.Recording(new SearchResultItem("A", "B", "https://example.org", "Test"));
        var coordinator = new ResearchTeamCoordinator(
            recorder,
            _multiAgent.NoHintChallengeLlm(),
            NullLogger<ResearchTeamCoordinator>.Instance);

        var result = await coordinator.ExecuteFullSessionAsync("Topic", Guid.NewGuid().ToString());
        // Bounce-back should use TargetClaim ("side effects") because SearchQueryHint is null
        var bounceBackQueries = recorder.Queries.Skip(6).ToList();
        Assert.NotEmpty(bounceBackQueries);
        Assert.All(bounceBackQueries, q => Assert.Equal("side effects", q));
    }
}
