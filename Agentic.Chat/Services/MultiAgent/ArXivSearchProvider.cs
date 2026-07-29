using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Services.MultiAgent;

public sealed class ArXivSearchProvider : ISearchProvider
{
    private static readonly Action<ILogger, string, Exception?> LogArXivFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, default, "ArXiv API search failed for '{Query}'.");

    private readonly HttpClient _httpClient;
    private readonly ILogger<ArXivSearchProvider> _logger;

    public ArXivSearchProvider(HttpClient httpClient, ILogger<ArXivSearchProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        try
        {
            var url = $"https://export.arxiv.org/api/query?search_query=all:{Uri.EscapeDataString(Sanitize(query))}&max_results=3";
            var xml = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            var doc = XDocument.Parse(xml);
            var ns = doc.Root!.GetDefaultNamespace();
            return doc.Root.Descendants(ns + "entry").Select(e => new SearchResultItem(
                e.Element(ns + "title")?.Value.Trim() ?? query,
                e.Element(ns + "summary")?.Value.Trim().Replace('\n', ' ')?.Length is int len
                    ? e.Element(ns + "summary")!.Value.Trim().Replace('\n', ' ')[..Math.Min(200, len)]
                    : "No abstract",
                $"https://arxiv.org/abs/{e.Element(ns + "id")?.Value.Split('/').Last() ?? "unknown"}",
                "ArXiv Open API"
            )).ToList();
        }
        catch (Exception ex)
        {
            LogArXivFailed(_logger, query, ex);
            return [];
        }
    }

    private static string Sanitize(string q) => q.Replace("+", " ").Replace("%20", " ");
}
