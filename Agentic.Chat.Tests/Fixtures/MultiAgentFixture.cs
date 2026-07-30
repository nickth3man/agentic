using System.Diagnostics.CodeAnalysis;
using System.Net;
using Agentic.Chat.Models.MultiAgent;
using Agentic.Chat.Services.MultiAgent;

namespace Agentic.Chat.Tests.Fixtures;

/// <summary>
/// Shared xUnit fixture for multi-agent tests. Centralizes the search-provider
/// and LLM-client test doubles that were previously duplicated across
/// MultiAgentProviderTests, MultiAgentCoverageTests, and ResearchTeamCoordinatorTests.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance methods preserve the xUnit IClassFixture usage pattern.")]
public sealed class MultiAgentFixture
{
    // ── Search providers ─────────────────────────────────────────────────

    public FixedResultSearchProvider FixedResult(params SearchResultItem[] items)
        => new(items);

    public EmptySearchProvider EmptySearch() => new();

    public ThrowingSearchProvider ThrowingSearch(string source = "test")
        => new(source);

    public ThrottledSearchProvider Throttled(int maxCalls = 4) => new(maxCalls);

    public AlwaysFailingSearchProvider AlwaysFailing(string message = "Network down")
        => new(message);

    public BounceBackFailingSearchProvider BounceBackFailing(int succeedCount = 6)
        => new(succeedCount);

    public RecordingSearchProvider Recording(SearchResultItem result)
        => new(result);

    public CompositeTestProvider TestProvider(params SearchResultItem[] items)
        => new(items);

    // ── LLM clients ──────────────────────────────────────────────────────

    public TestLlmClient TestLlm(string? prefix = null) => new(prefix);

    public PassThroughLlmClient PassThroughLlm() => new();

    public ChallengeLlmClient ChallengeLlm() => new();

    public NoChallengeLlmClient NoChallengeLlm() => new();

    public InvalidJsonLlmClient InvalidJsonLlm() => new();

    public UnknownRoleChallengeLlmClient UnknownRoleChallengeLlm() => new();

    public NoHintChallengeLlmClient NoHintChallengeLlm() => new();

    // ── Search-provider doubles ──────────────────────────────────────────

    /// <summary>
    /// Returns fixed items on every query. Used to simulate a successful search.
    /// </summary>
    public sealed class FixedResultSearchProvider : ISearchProvider
    {
        private readonly List<SearchResultItem> _items;

        public FixedResultSearchProvider(params SearchResultItem[] items)
            => _items = items.ToList();

        public Task<List<SearchResultItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.ToList());
    }

    /// <summary>
    /// Always returns an empty result list.
    /// </summary>
    public sealed class EmptySearchProvider : ISearchProvider
    {
        public Task<List<SearchResultItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new List<SearchResultItem>());
    }

    /// <summary>
    /// Throws on every search, exercising error-handling paths.
    /// </summary>
    public sealed class ThrowingSearchProvider : ISearchProvider
    {
        public ThrowingSearchProvider(string source) => Source = source;

        public string Source { get; }

        public Task<List<SearchResultItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Simulated CORS failure for " + Source);
    }

    /// <summary>
    /// Returns results for the first <paramref name="maxCalls"/> queries, then empty.
    /// </summary>
    public sealed class ThrottledSearchProvider : ISearchProvider
    {
        private int _callCount;
        private readonly int _maxCalls;

        public ThrottledSearchProvider(int maxCalls = 4) => _maxCalls = maxCalls;

        public Task<List<SearchResultItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount > _maxCalls)
            {
                return Task.FromResult(new List<SearchResultItem>());
            }

            return Task.FromResult(new List<SearchResultItem>
            {
                new SearchResultItem($"R{_callCount}", "snippet", $"https://x.com/{_callCount}", "Test")
            });
        }
    }

    /// <summary>
    /// Always throws, simulating a hard network failure.
    /// </summary>
    public sealed class AlwaysFailingSearchProvider : ISearchProvider
    {
        private readonly string _message;

        public AlwaysFailingSearchProvider(string message = "Network down") => _message = message;

        public Task<List<SearchResultItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(_message);
    }

    /// <summary>
    /// Succeeds for the first <paramref name="succeedCount"/> calls, then throws.
    /// Exercises the bounce-back search-error path.
    /// </summary>
    public sealed class BounceBackFailingSearchProvider : ISearchProvider
    {
        private int _callCount;
        private readonly int _succeedCount;

        public BounceBackFailingSearchProvider(int succeedCount = 6)
            => _succeedCount = succeedCount;

        public Task<List<SearchResultItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount <= _succeedCount)
            {
                return Task.FromResult(new List<SearchResultItem>
                {
                    new SearchResultItem("Result", "Snippet", "https://example.org", "Test")
                });
            }

            throw new InvalidOperationException("Bounce-back network failure");
        }
    }

    /// <summary>
    /// Records every query passed to SearchAsync for later inspection.
    /// </summary>
    public sealed class RecordingSearchProvider : ISearchProvider
    {
        private readonly SearchResultItem _result;
        private readonly List<string> _queries = [];

        public RecordingSearchProvider(SearchResultItem result) => _result = result;

        public IReadOnlyList<string> Queries => _queries;

        public Task<List<SearchResultItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            _queries.Add(query);
            return Task.FromResult(new List<SearchResultItem> { _result });
        }
    }

    /// <summary>
    /// Simple provider used for CompositeSearchProvider composition tests.
    /// </summary>
    public sealed class CompositeTestProvider : ISearchProvider
    {
        private readonly SearchResultItem[] _items;

        public CompositeTestProvider(SearchResultItem[] items) => _items = items;

        public Task<List<SearchResultItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.ToList());
    }

    // ── LLM-client doubles ───────────────────────────────────────────────

    /// <summary>
    /// Returns a generic analysis string for every prompt.
    /// </summary>
    public sealed class TestLlmClient : ILocalLlmClient
    {
        private readonly string? _prefix;

        public TestLlmClient(string? prefix = null) => _prefix = prefix;

        public Task<string> GenerateCompletionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_prefix is null ? $"Analysis for: {userPrompt}" : $"{_prefix}: {userPrompt}");
    }

    /// <summary>
    /// Echoes the user prompt back unchanged.
    /// </summary>
    public sealed class PassThroughLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
            => Task.FromResult(userPrompt);
    }

    /// <summary>
    /// Returns a JSON Challenge object when acting as Skeptic/Fact Verifier,
    /// exercising the bounce-back branch.
    /// </summary>
    public sealed class ChallengeLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
            {
                return Task.FromResult(
                    "{\"target\":\"side effects\",\"question\":\"deeper evidence needed\",\"role\":\"📚\",\"hint\":\"side effects research papers\"}");
            }

            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>
    /// Returns "No further challenges." when acting as Skeptic/Fact Verifier.
    /// </summary>
    public sealed class NoChallengeLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
            {
                return Task.FromResult("No further challenges.");
            }

            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>
    /// Returns invalid JSON when acting as Skeptic/Fact Verifier.
    /// </summary>
    public sealed class InvalidJsonLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
            {
                return Task.FromResult("{invalid}");
            }

            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>
    /// Returns a challenge with an unknown role when acting as Skeptic/Fact Verifier.
    /// </summary>
    public sealed class UnknownRoleChallengeLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
            {
                return Task.FromResult(
                    "{\"target\":\"miss\",\"question\":\"why\",\"role\":\"❓UnknownRole\",\"hint\":\"search hint\"}");
            }

            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }

    /// <summary>
    /// Returns a challenge JSON without the "hint" field when acting as Skeptic/Fact Verifier.
    /// </summary>
    public sealed class NoHintChallengeLlmClient : ILocalLlmClient
    {
        public Task<string> GenerateCompletionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("Skeptic") || systemPrompt.Contains("Fact Verifier"))
            {
                return Task.FromResult(
                    "{\"target\":\"side effects\",\"question\":\"deeper evidence needed\",\"role\":\"📚\"}");
            }

            return Task.FromResult($"Analysis for: {userPrompt}");
        }
    }
}
