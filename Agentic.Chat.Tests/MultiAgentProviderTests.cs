using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Chat.Models.MultiAgent;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agentic.Chat.Tests;

public sealed class ProviderSuccessPathTests
{
    [Fact]
    public async Task SearXNG_ParsesValidResponse()
    {
        var json = "{\"results\":[{\"title\":\"T1\",\"url\":\"https://a\",\"content\":\"Snippet A\",\"engine\":\"ddg\"}]}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        var results = await provider.SearchAsync("test");
        Assert.Single(results);
        Assert.Equal("T1", results[0].Title);
        Assert.Equal("https://a", results[0].Url);
        Assert.Equal("ddg", results[0].SourceEngine);
    }

    [Fact]
    public async Task SearXNG_EmptyQueryReturnsEmpty()
    {
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        Assert.Empty(await provider.SearchAsync(""));
    }

    [Fact]
    public async Task SearXNG_NonSuccessReturnsEmpty()
    {
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        Assert.Empty(await provider.SearchAsync("test"));
    }

    [Fact]
    public async Task SearXNG_UsesSnippetWhenContentMissing()
    {
        // SearXNG item with no "content" field, only "snippet" — exercises the
        // r.Snippet fallback branch in SearXNGSearchProvider (and covers the
        // Snippet property getter on the DTO).
        var json = "{\"results\":[{\"title\":\"Only Snippet\",\"url\":\"https://a\",\"snippet\":\"Snippet-only fallback\",\"engine\":\"ddg\"}]}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        var results = await provider.SearchAsync("test");
        Assert.Single(results);
        Assert.Equal("Snippet-only fallback", results[0].Snippet);
    }

    [Fact]
    public async Task SearXNG_EmptyResultsReturnsEmpty()
    {
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"results\":[]}", Encoding.UTF8, "application/json")
        }));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        Assert.Empty(await provider.SearchAsync("test"));
    }

    [Fact]
    public async Task SearXNG_HandlesNonStringPropertiesGracefully()
    {
        // SearXNG result with title/snippet but missing url/content/engine — exercises
        // the r.TryGetProperty && ValueKind == String false branch and the ?? fallbacks.
        var json = "{\"results\":[{\"title\":\"X\",\"url\":123,\"content\":true}]}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        var results = await provider.SearchAsync("test");
        Assert.Single(results);
        Assert.Equal("https://searxng.local", results[0].Url);
        Assert.Equal("No snippet available", results[0].Snippet);
        Assert.Equal("searxng", results[0].SourceEngine);
    }

    [Fact]
    public async Task SearXNG_HandlesMissingResultsProperty()
    {
        // No "results" property at all — JsonDocument returns false from TryGetProperty.
        var json = "{\"query\":\"x\"}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        Assert.Empty(await provider.SearchAsync("test"));
    }

    [Fact]
    public async Task SearXNG_HandlesNonArrayResults()
    {
        // "results" is a string, not an array — branch on results.ValueKind == Array.
        var json = "{\"results\":\"not an array\"}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        Assert.Empty(await provider.SearchAsync("test"));
    }

    [Fact]
    public async Task SearXNG_CapsAtFiveItems()
    {
        // 7 results in the array — only first 5 should be returned (items.Count >= 5 break).
        var json = "{\"results\":[";
        for (var i = 0; i < 7; i++)
            json += (i > 0 ? "," : "") + "{\"title\":\"T" + i + "\",\"url\":\"https://a\",\"engine\":\"e\"}";
        json += "]}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var provider = new SearXNGSearchProvider(client, NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        var results = await provider.SearchAsync("test");
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void SearXNG_ConstructorRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new SearXNGSearchProvider(null!, NullLogger<SearXNGSearchProvider>.Instance, "http://x"));
        Assert.Throws<ArgumentNullException>(() => new SearXNGSearchProvider(new HttpClient(), null!, "http://x"));
    }

    [Fact]
    public void Wikipedia_ConstructorRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new WikipediaSearchProvider(null!, NullLogger<WikipediaSearchProvider>.Instance));
        Assert.Throws<ArgumentNullException>(() => new WikipediaSearchProvider(new HttpClient(), null!));
    }

    [Fact]
    public void ArXiv_ConstructorRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ArXivSearchProvider(null!, NullLogger<ArXivSearchProvider>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ArXivSearchProvider(new HttpClient(), null!));
    }

    [Fact]
    public void Ollama_ConstructorRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new OllamaLocalLlmClient(null!, NullLogger<OllamaLocalLlmClient>.Instance, "http://x", "m"));
        Assert.Throws<ArgumentNullException>(() => new OllamaLocalLlmClient(new HttpClient(), null!, "http://x", "m"));
    }

    [Fact]
    public void CompositeSearchProvider_ConstructorRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeSearchProvider(null!));
    }

    [Fact]
    public void ResearchTeamCoordinator_ConstructorRejectsNullArguments()
    {
        var sp = new SearXNGSearchProvider(new HttpClient(), NullLogger<SearXNGSearchProvider>.Instance, "http://x");
        var llm = new OllamaLocalLlmClient(new HttpClient(), NullLogger<OllamaLocalLlmClient>.Instance, "http://x", "m");
        Assert.Throws<ArgumentNullException>(() => new ResearchTeamCoordinator(null!, llm, NullLogger<ResearchTeamCoordinator>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ResearchTeamCoordinator(sp, null!, NullLogger<ResearchTeamCoordinator>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ResearchTeamCoordinator(sp, llm, null!));
    }

    [Fact]
    public async Task Wikipedia_ParsesValidResponse()
    {
        var json = "{\"query\":{\"search\":[{\"title\":\"A\",\"snippet\":\"snippet a\"}]}}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var provider = new WikipediaSearchProvider(client, NullLogger<WikipediaSearchProvider>.Instance);
        var results = await provider.SearchAsync("test");
        Assert.Single(results);
        Assert.Equal("A", results[0].Title);
        Assert.Contains("wikipedia.org", results[0].Url);
    }

    [Fact]
    public async Task Wikipedia_NonSuccessReturnsEmpty()
    {
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var provider = new WikipediaSearchProvider(client, NullLogger<WikipediaSearchProvider>.Instance);
        Assert.Empty(await provider.SearchAsync("test"));
    }

    [Fact]
    public async Task Wikipedia_HandlesNonArrayResults()
    {
        // Wikipedia response without query.search (different shape) → doc?.Query?.Search?.Count > 0 is false.
        var json = "{\"error\":{\"info\":\"x\"}}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var provider = new WikipediaSearchProvider(client, NullLogger<WikipediaSearchProvider>.Instance);
        Assert.Empty(await provider.SearchAsync("test"));
    }

    [Fact]
    public async Task ArXiv_ParsesValidResponse()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                  "<feed xmlns=\"http://www.w3.org/2005/Atom\">" +
                  "<entry><id>urn:uuid:1</id><title>Test Paper 1</title><summary>Summary of paper one goes here.</summary></entry>" +
                  "<entry><id>urn:uuid:2</id><title>Test Paper 2</title><summary>Summary of paper two goes here.</summary></entry>" +
                  "</feed>";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/atom+xml")
        }));
        var provider = new ArXivSearchProvider(client, NullLogger<ArXivSearchProvider>.Instance);
        var results = await provider.SearchAsync("quantum");
        Assert.Equal(2, results.Count);
        Assert.Equal("Test Paper 1", results[0].Title);
        Assert.Contains("arxiv.org", results[0].Url);
    }

    [Fact]
    public async Task ArXiv_NonSuccessReturnsEmpty()
    {
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var provider = new ArXivSearchProvider(client, NullLogger<ArXivSearchProvider>.Instance);
        Assert.Empty(await provider.SearchAsync("test"));
    }

    [Fact]
    public async Task Ollama_ParsesValidResponse()
    {
        var json = "{\"response\":\"This is the LLM output.\"}";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var llm = new OllamaLocalLlmClient(client, NullLogger<OllamaLocalLlmClient>.Instance, "http://x");
        var result = await llm.GenerateCompletionAsync("sys", "user");
        Assert.Equal("This is the LLM output.", result);
    }

    [Fact]
    public async Task Ollama_NonSuccessReturnsUnavailable()
    {
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var llm = new OllamaLocalLlmClient(client, NullLogger<OllamaLocalLlmClient>.Instance, "http://x");
        var result = await llm.GenerateCompletionAsync("sys", "user");
        Assert.Equal("[Local LLM unavailable]", result);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public StubHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }
}

public sealed class AgentActivityFrameCoverageTests
{
    [Fact]
    public void Frame_AllPropertiesAccessible()
    {
        var frame = new AgentActivityFrame
        {
            SessionId = "sid",
            StepIndex = 1,
            SenderAgent = "A",
            RecipientAgent = "B",
            DivisionName = "D",
            ActionKind = "K",
            ProgressSummary = "S",
            Payload = "P",
            StatusBadge = ClaimVerificationStatus.Unresolved
        };
        Assert.Equal("sid", frame.SessionId);
        Assert.Equal(1, frame.StepIndex);
        Assert.Equal("A", frame.SenderAgent);
        Assert.Equal("B", frame.RecipientAgent);
        Assert.Equal("D", frame.DivisionName);
        Assert.Equal("K", frame.ActionKind);
        Assert.Equal("S", frame.ProgressSummary);
        Assert.Equal("P", frame.Payload);
        Assert.NotEqual(Guid.Empty, frame.Id);
        Assert.NotEqual(default, frame.Timestamp);
        Assert.Equal(ClaimVerificationStatus.Unresolved, frame.StatusBadge);

        Assert.False(frame.IsExpanded);
        frame.IsExpanded = true;
        Assert.True(frame.IsExpanded);
    }

    [Fact]
    public void SearchResultItem_AllPropertiesAccessible()
    {
        var item = new SearchResultItem("T", "S", "https://u", "E");
        Assert.Equal("T", item.Title);
        Assert.Equal("S", item.Snippet);
        Assert.Equal("https://u", item.Url);
        Assert.Equal("E", item.SourceEngine);
    }

    [Fact]
    public void ResearchClaim_AllPropertiesAccessible()
    {
        var c = new ResearchClaim("text", ClaimVerificationStatus.Unresolved, "expl", new List<string> { "src" });
        Assert.Equal("text", c.ClaimText);
        Assert.Equal("expl", c.Explanation);
        Assert.Single(c.Sources);
    }

    [Fact]
    public void TimelineEvent_AllPropertiesAccessible()
    {
        var t = new TimelineEvent("p", "title", "desc");
        Assert.Equal("p", t.PeriodOrDate);
        Assert.Equal("title", t.Title);
        Assert.Equal("desc", t.Description);
    }

    [Fact]
    public void ResearchFaq_AllPropertiesAccessible()
    {
        var f = new ResearchFaq("q?", "a.");
        Assert.Equal("q?", f.Question);
        Assert.Equal("a.", f.Answer);
    }

    [Fact]
    public void VerifiedCitation_AllPropertiesAccessible()
    {
        var v = new VerifiedCitation("title", "https://x.com", "x.com", ClaimVerificationStatus.Unresolved);
        Assert.Equal("title", v.SourceTitle);
        Assert.Equal("https://x.com", v.Url);
        Assert.Equal("x.com", v.Domain);
        Assert.Equal(ClaimVerificationStatus.Unresolved, v.TrustRating);
    }
}
