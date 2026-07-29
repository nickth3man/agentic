using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Services.MultiAgent;

public sealed class WikipediaSearchProvider : ISearchProvider
{
    private static readonly Action<ILogger, string, Exception?> LogWikiSearchFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, default, "Wikipedia Open API search failed for query '{Query}'.");

    private readonly HttpClient _httpClient;
    private readonly ILogger<WikipediaSearchProvider> _logger;

    public WikipediaSearchProvider(HttpClient httpClient, ILogger<WikipediaSearchProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    public async Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        try
        {
            var url = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&format=json&origin=*";
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<WikiResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
                if (data?.Query?.Search != null && data.Query.Search.Count > 0)
                {
                    var list = new List<SearchResultItem>();
                    foreach (var item in data.Query.Search)
                    {
                        var cleanSnippet = System.Net.WebUtility.HtmlDecode(item.Snippet?.Replace("<span class=\"searchmatch\">", "").Replace("</span>", "") ?? "");
                        list.Add(new SearchResultItem(
                            item.Title ?? query,
                            cleanSnippet,
                            $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(item.Title ?? query)}",
                            "Wikipedia Open API"
                        ));
                        if (list.Count >= 3) break;
                    }
                    return list;
                }
            }
        }
        catch (Exception ex)
        {
            LogWikiSearchFailed(_logger, query, ex);
        }

        return [];
    }

    private sealed record WikiResponse([property: JsonPropertyName("query")] WikiQueryData? Query);
    private sealed record WikiQueryData([property: JsonPropertyName("search")] List<WikiSearchItem>? Search);
    private sealed record WikiSearchItem(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("snippet")] string? Snippet
    );
}
