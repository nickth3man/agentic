using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Services.MultiAgent;

/// <summary>
/// Fans a single query out to every registered <see cref="ISearchProvider"/> and
/// combines the results. Each provider's call is independently guarded so a
/// single failing child (typically a browser CORS rejection on Pages for
/// providers like ArXiv that don't ship ACAO) cannot poison the others or
/// fail the whole research session.
/// </summary>
public sealed class CompositeSearchProvider : ISearchProvider
{
    private static readonly Action<ILogger, string, string, Exception?> LogProviderFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, default, "Search provider {ProviderType} failed for query '{Query}'.");

    private readonly IEnumerable<ISearchProvider> _providers;
    private readonly ILogger<CompositeSearchProvider>? _logger;

    public CompositeSearchProvider(
        IEnumerable<ISearchProvider> providers,
        ILogger<CompositeSearchProvider>? logger = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger;
    }

    public async Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // Each provider's call is wrapped in its own try/catch so a single failing
        // child can't poison the others. Task.WhenAll surfaces the FIRST exception
        // and drops the rest, which is the wrong behavior for a fan-out aggregator.
        var tasks = _providers.Select(p => SafeSearchAsync(p, query, cancellationToken)).ToList();
        var perProviderResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        var combined = new List<SearchResultItem>();
        foreach (var list in perProviderResults)
        {
            if (list is null) continue;
            combined.AddRange(list);
        }
        return combined;
    }

    private async Task<List<SearchResultItem>> SafeSearchAsync(
        ISearchProvider provider,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            return result ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // propagate explicit cancellation
        }
        catch (Exception ex)
        {
            LogProviderFailed(_logger!, provider.GetType().Name, query, ex);
            return [];
        }
    }
}
