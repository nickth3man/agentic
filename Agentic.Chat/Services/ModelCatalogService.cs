using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agentic.Chat.Models;

namespace Agentic.Chat.Services;

public sealed class ModelCatalogService
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<OpenRouterModel>? _cached;
    private DateTimeOffset _cachedAt;

    public ModelCatalogService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<OpenRouterModel>> GetModelsAsync(CancellationToken ct = default)
    {
        // Fast path: cache hit before touching the lock so steady-state reads are lock-free.
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
        {
            return _cached;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the lock — another caller may have just populated it.
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            {
                return _cached;
            }

            IReadOnlyList<OpenRouterModel> fresh;
            try
            {
                fresh = await FetchAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Don't swallow user-initiated cancellation.
                throw;
            }
            catch
            {
                // Network failure tolerated only when there's a cached value to fall back on.
                if (_cached is not null)
                {
                    return _cached;
                }
                throw;
            }

            _cached = fresh;
            _cachedAt = DateTimeOffset.UtcNow;
            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpenRouterModel?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        // The model picker hands us the trigger value verbatim (possibly padded with
        // whitespace from a UI click target); trim before matching so the lookup is
        // tolerant. The comparison itself is case-insensitive so the picker UI can
        // open-cased Id values without the underlying catalog needing to know.
        var trimmed = id.Trim();
        var models = await GetModelsAsync(ct).ConfigureAwait(false);
        foreach (var model in models)
        {
            if (string.Equals(model.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }
        return null;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fresh = await FetchAsync(ct).ConfigureAwait(false);
            _cached = fresh;
            _cachedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Test seam: rewinds _cachedAt by the given delta so the TTL cache can be made to look
    // stale instantly. Exposed as `internal` via the assembly's InternalsVisibleTo("Agentic.Chat.Tests").
    internal void FastForwardCache(TimeSpan delta) => _cachedAt = _cachedAt - delta;

    // Test seam: directly primes the cache with the given list, bypassing the HTTP
    // fetch path. Used by callers that need the catalog to resolve specific models
    // without spinning up a stubbed HttpClient.
    internal void SeedForTest(IReadOnlyList<OpenRouterModel> models)
    {
        _cached = models;
        _cachedAt = DateTimeOffset.UtcNow;
    }

    private async Task<IReadOnlyList<OpenRouterModel>> FetchAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OpenRouter");
        using var stream = await client.GetStreamAsync("models", ct).ConfigureAwait(false);
        var envelope = await JsonSerializer
            .DeserializeAsync<ModelListEnvelope>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        if (envelope?.Data is null || envelope.Data.Count == 0)
        {
            return Array.Empty<OpenRouterModel>();
        }
        var list = new List<OpenRouterModel>(envelope.Data.Count);
        foreach (var element in envelope.Data)
        {
            list.Add(ParseModel(element));
        }
        return list;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Public-parse helper for tests. Translates the raw DTO shape into the public
    // OpenRouterModel record, mapping epoch-seconds → DateTimeOffset and the nested
    // architecture.modality → flat string. Unit-tested directly with a representative
    // JSON literal in ModelCatalogServiceTests.
    internal static OpenRouterModel ParseModel(JsonElement element)
    {
        var raw = element.Deserialize<RawModel>(JsonOptions)
            ?? throw new InvalidOperationException("Empty model element.");
        var prompt = decimal.Parse(
            raw.Pricing?.Prompt ?? "0", CultureInfo.InvariantCulture);
        var completion = decimal.Parse(
            raw.Pricing?.Completion ?? "0", CultureInfo.InvariantCulture);
        var supported = raw.SupportedParameters is null
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : raw.SupportedParameters.ToList();
        return new OpenRouterModel(
            Id: raw.Id ?? string.Empty,
            Name: raw.Name ?? string.Empty,
            ContextLength: raw.ContextLength,
            Created: DateTimeOffset.FromUnixTimeSeconds(raw.Created),
            Modality: raw.Architecture?.Modality ?? string.Empty,
            Pricing: new OpenRouterPricing(prompt, completion),
            SupportedParameters: supported);
    }

    private sealed class ModelListEnvelope
    {
        [JsonPropertyName("data")]
        public List<JsonElement>? Data { get; set; }
    }

    private sealed class RawModel
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("context_length")] public long ContextLength { get; set; }
        [JsonPropertyName("created")] public long Created { get; set; }
        [JsonPropertyName("architecture")] public RawArchitecture? Architecture { get; set; }
        [JsonPropertyName("pricing")] public RawPricing? Pricing { get; set; }
        [JsonPropertyName("supported_parameters")] public List<string>? SupportedParameters { get; set; }
    }

    private sealed class RawArchitecture
    {
        [JsonPropertyName("modality")] public string? Modality { get; set; }
    }

    private sealed class RawPricing
    {
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "0";
        [JsonPropertyName("completion")] public string Completion { get; set; } = "0";
    }
}
