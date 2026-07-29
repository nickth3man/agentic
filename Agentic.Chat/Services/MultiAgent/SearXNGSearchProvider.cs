using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Services.MultiAgent;

public sealed class SearXNGSearchProvider : ISearchProvider
{
    private static readonly Action<ILogger, string, Exception?> LogSearchUnavailable =
        LoggerMessage.Define<string>(LogLevel.Warning, default, "SearXNG search unavailable for query '{Query}'. Returning empty search results.");

    private readonly HttpClient _httpClient;
    private readonly ILogger<SearXNGSearchProvider> _logger;
    private readonly string _baseUrl;

    public SearXNGSearchProvider(HttpClient httpClient, ILogger<SearXNGSearchProvider> logger, string baseUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var requestUrl = $"{_baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json";
            var response = await _httpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<SearchResultItem>();
                    foreach (var r in results.EnumerateArray())
                    {
                        var title = r.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                        var url = r.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                        var content = r.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        var snippet = r.TryGetProperty("snippet", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                        var engine = r.TryGetProperty("engine", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

                        items.Add(new SearchResultItem(
                            title ?? query,
                            content ?? snippet ?? "No snippet available",
                            url ?? "https://searxng.local",
                            engine ?? "searxng"
                        ));
                        if (items.Count >= 5) break;
                    }
                    return items;
                }
            }
        }
        catch (Exception ex)
        {
            LogSearchUnavailable(_logger, query, ex);
        }

        return [];
    }
}
