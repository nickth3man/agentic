using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Services.MultiAgent;

/// <summary>
/// Browser-friendly, no-key web search via the public mwmbl.org API (AGPL-3.0).
/// <see href="https://github.com/mwmbl/mwmbl"/> — volunteer-curated index of the
/// small/indie web plus Wikipedia. Public endpoint returns <c>Access-Control-Allow-Origin: *</c>,
/// https only, no auth required.
/// </summary>
public sealed class MwmblSearchProvider : ISearchProvider
{
    private const string BaseUrl = "https://api.mwmbl.org";
    private const int MaxResults = 5;

    private static readonly Action<ILogger, string, Exception?> LogMwmblFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, default, "Mwmbl search failed for query '{Query}'.");

    private readonly HttpClient _httpClient;
    private readonly ILogger<MwmblSearchProvider> _logger;

    public MwmblSearchProvider(HttpClient httpClient, ILogger<MwmblSearchProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        try
        {
            var url = $"{BaseUrl}/search/?s={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];

            var hits = await response.Content
                .ReadFromJsonAsync<List<MwmblHit>>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (hits is null || hits.Count == 0) return [];

            var results = new List<SearchResultItem>(Math.Min(hits.Count, MaxResults));
            foreach (var hit in hits)
            {
                if (string.IsNullOrWhiteSpace(hit.Url)) continue;
                results.Add(new SearchResultItem(
                    Title: FlattenFragments(hit.Title),
                    Snippet: FlattenFragments(hit.Extract),
                    Url: hit.Url!,
                    SourceEngine: string.IsNullOrWhiteSpace(hit.Source) ? "Mwmbl" : $"Mwmbl·{hit.Source}"));
                if (results.Count >= MaxResults) break;
            }
            return results;
        }
        catch (Exception ex)
        {
            LogMwmblFailed(_logger, query, ex);
            return [];
        }
    }

    private static string FlattenFragments(List<MwmblFragment>? fragments)
    {
        if (fragments is null || fragments.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var f in fragments)
        {
            if (!string.IsNullOrEmpty(f.Value)) sb.Append(f.Value);
        }
        return sb.ToString();
    }

    private sealed class MwmblHit
    {
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("title")] public List<MwmblFragment>? Title { get; init; }
        [JsonPropertyName("extract")] public List<MwmblFragment>? Extract { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
    }

    private sealed class MwmblFragment
    {
        [JsonPropertyName("value")] public string? Value { get; init; }
        [JsonPropertyName("is_bold")] public bool IsBold { get; init; }
    }
}
