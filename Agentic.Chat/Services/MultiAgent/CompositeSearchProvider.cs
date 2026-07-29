using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Agentic.Chat.Services.MultiAgent;

public sealed class CompositeSearchProvider : ISearchProvider
{
    private readonly IEnumerable<ISearchProvider> _providers;

    public CompositeSearchProvider(IEnumerable<ISearchProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var tasks = _providers.Select(p => p.SearchAsync(query, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var combined = new List<SearchResultItem>();
        foreach (var list in results)
        {
            if (list != null)
            {
                combined.AddRange(list);
            }
        }
        return combined;
    }
}
