using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public SearXNGSearchProvider(HttpClient httpClient, ILogger<SearXNGSearchProvider> logger, string baseUrl = "http://localhost:8080")
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
                var payload = await response.Content.ReadFromJsonAsync<SearXNGResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
                if (payload?.Results != null && payload.Results.Count > 0)
                {
                    var items = new List<SearchResultItem>();
                    foreach (var r in payload.Results)
                    {
                        items.Add(new SearchResultItem(
                            r.Title ?? query,
                            r.Content ?? r.Snippet ?? "No snippet available",
                            r.Url ?? "https://searxng.local",
                            r.Engine ?? "searxng"
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

    private sealed record SearXNGResponse(
        [property: JsonPropertyName("results")] List<SearXNGItem>? Results
    );

    private sealed record SearXNGItem(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("snippet")] string? Snippet,
        [property: JsonPropertyName("engine")] string? Engine
    );
}
