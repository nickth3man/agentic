using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Chat.Models.MultiAgent;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agentic.Chat.Tests;

public sealed class CompositeSearchProviderTests
{
    [Fact]
    public async Task Composite_ReturnsAggregatedResults()
    {
        var items1 = new[] { new SearchResultItem("A", "a", "https://a", "P1") };
        var items2 = new[] { new SearchResultItem("B", "b", "https://b", "P2") };
        var p1 = new TestProvider(items1);
        var p2 = new TestProvider(items2);
        var comp = new CompositeSearchProvider([p1, p2]);

        var results = await comp.SearchAsync("test");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Composite_ReturnsEmpty_WhenAllProvidersReturnEmpty()
    {
        var comp = new CompositeSearchProvider([
            new TestProvider([]),
            new TestProvider([])
        ]);
        var results = await comp.SearchAsync("void");
        Assert.Empty(results);
    }

    private sealed class TestProvider : ISearchProvider
    {
        private readonly SearchResultItem[] _items;
        public TestProvider(SearchResultItem[] items) => _items = items;
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken ct = default)
            => Task.FromResult(_items.ToList());
    }

    private sealed class ThrowingSearchProvider : ISearchProvider
    {
        public ThrowingSearchProvider(string source) => Source = source;
        public string Source { get; }
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Simulated CORS failure for " + query);
    }

    [Fact]
    public async Task Composite_IsolatesFailingChild_FromSuccessfulSiblings()
    {
        var wikipediaHit = new SearchResultItem("Test-wikipedia", "climate change snippet", "https://en.wikipedia.org/wiki/Climate_change", "Wikipedia");
        var mwmblHit = new SearchResultItem("Test-mwmbl", "mwmbl snippet", "https://mwmbl.org/", "Mwmbl");
        var composite = new CompositeSearchProvider(
            new ISearchProvider[]
            {
                new TestProvider(new[] { wikipediaHit }),
                new ThrowingSearchProvider("bad"),
                new TestProvider(new[] { mwmblHit }),
            },
            NullLogger<CompositeSearchProvider>.Instance);

        var results = await composite.SearchAsync("climate change");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Title == "Test-wikipedia");
        Assert.Contains(results, r => r.Title == "Test-mwmbl");
        Assert.Equal(2, results.Count(r => r.Title.StartsWith("Test-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Composite_AllFailingChildren_ProduceEmptyAggregate()
    {
        var composite = new CompositeSearchProvider(
            new ISearchProvider[]
            {
                new ThrowingSearchProvider("a"),
                new ThrowingSearchProvider("b"),
            },
            NullLogger<CompositeSearchProvider>.Instance);

        var results = await composite.SearchAsync("climate change");

        Assert.Empty(results);
    }
}

public sealed class SearchProviderErrorTests
{
    [Fact]
    public async Task SearXNG_ReturnsEmpty_OnConnectionError()
    {
        var client = new HttpClient(new FailingHandler()) { Timeout = TimeSpan.FromSeconds(1) };
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://localhost:1");
        var results = await provider.SearchAsync("test");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Wikipedia_ReturnsEmpty_OnConnectionError()
    {
        var client = new HttpClient(new FailingHandler()) { Timeout = TimeSpan.FromSeconds(1) };
        var provider = new WikipediaSearchProvider(client, NullLogger<WikipediaSearchProvider>.Instance);
        var results = await provider.SearchAsync("test");
        Assert.Empty(results);
    }

    [Fact]
    public async Task ArXiv_ReturnsEmpty_OnConnectionError()
    {
        var client = new HttpClient(new FailingHandler()) { Timeout = TimeSpan.FromSeconds(1) };
        var provider = new ArXivSearchProvider(client, NullLogger<ArXivSearchProvider>.Instance);
        var results = await provider.SearchAsync("test");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Ollama_GeneratesUnavailable_OnConnectionError()
    {
        var client = new HttpClient(new FailingHandler()) { Timeout = TimeSpan.FromSeconds(1) };
        var llm = new OllamaLocalLlmClient(client, NullLogger<OllamaLocalLlmClient>.Instance, "http://localhost:1");
        var result = await llm.GenerateCompletionAsync("system", "user prompt");
        Assert.Equal("[Local LLM unavailable]", result);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network error");
    }
}

public sealed class CoordinatorBranchTests
{
    [Fact]
    public async Task Coordinator_EmitsFrames_WhenAllProvidersFail()
    {
        var failProvider = new FailProvider();
        var coordinator = new ResearchTeamCoordinator(failProvider, new PassThroughLlm(), NullLogger<ResearchTeamCoordinator>.Instance);
        var result = await coordinator.ExecuteFullSessionAsync("test", "sid");
        Assert.NotEmpty(result.Frames);
        Assert.Empty(result.SearchItems);
    }

    [Fact]
    public async Task Coordinator_CollectsItems_WhenProvidersSucceed()
    {
        var okProvider = new FixedResultProvider(new SearchResultItem("X", "x", "https://x", "Src"));
        var coordinator = new ResearchTeamCoordinator(okProvider, new PassThroughLlm(), NullLogger<ResearchTeamCoordinator>.Instance);
        var result = await coordinator.ExecuteFullSessionAsync("test", "sid");
        Assert.NotEmpty(result.SearchItems);
        _ = coordinator.BuildDashboard("test", result.Frames.ToList(), result.SearchItems.ToList());
    }

    [Fact]
    public async Task BuildDashboard_HandlesNullSearchItems()
    {
        var coordinator = new ResearchTeamCoordinator(new FailProvider(), new PassThroughLlm(), NullLogger<ResearchTeamCoordinator>.Instance);
        var dash = coordinator.BuildDashboard("test", [], null!);
        Assert.NotNull(dash);
        Assert.Single(dash.Claims);
        Assert.Equal("test", dash.UserTopic);
        Assert.NotEmpty(dash.ExecutiveSummary);
        Assert.NotEmpty(dash.KeyTakeaways);
        Assert.NotEmpty(dash.Timeline);
        Assert.NotEmpty(dash.Faqs);
        Assert.NotEmpty(dash.UnresolvedQuestions);
        Assert.Equal(3, dash.RoundsCompleted);
        Assert.Equal(0, dash.SearchQueriesExecuted);
        var c = dash.Claims[0];
        Assert.NotEmpty(c.ClaimText);
        Assert.NotEmpty(c.Explanation);
        Assert.Empty(c.Sources); // no search data → no sources
    }

    private sealed class FailProvider : ISearchProvider
    {
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken ct = default)
            => throw new HttpRequestException("fail");
    }

    private sealed class FixedResultProvider : ISearchProvider
    {
        private readonly SearchResultItem _item;
        public FixedResultProvider(SearchResultItem item) => _item = item;
        public Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken ct = default)
            => Task.FromResult(new List<SearchResultItem> { _item });
    }

    private sealed class PassThroughLlm : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(string system, string user, CancellationToken ct = default)
            => Task.FromResult(user);
    }
}
