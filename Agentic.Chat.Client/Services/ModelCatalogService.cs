using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agentic.Chat.Models;

namespace Agentic.Chat.Services;

public sealed class ModelCatalogService(
    HttpClient http,
    OpenRouterCredentialService credentials,
    ILogger<ModelCatalogService> logger) : IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenRouterCredentialService _credentials = credentials;
    private readonly HttpClient _http = http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<OpenRouterModel>? _cached;
    private DateTimeOffset _cachedAt;
    private bool _cachedForUserKey;

    public async Task<IReadOnlyList<OpenRouterModel>> GetModelsAsync(CancellationToken ct = default)
    {
        await _credentials.InitializeAsync().ConfigureAwait(false);
        if (IsCacheFresh())
        {
            return _cached!;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsCacheFresh())
            {
                return _cached!;
            }

            try
            {
                await RefreshCoreAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
                when (_cached is not null && _cachedForUserKey == _credentials.HasUserKey)
            {
                ClientLog.Warning(
                    logger,
                    exception,
                    "Using a stale OpenRouter model catalog after refresh failed.");
            }

            return _cached ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpenRouterModel?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return (await GetModelsAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(model => string.Equals(model.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _credentials.InitializeAsync().ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsCacheFresh()
        => _cached is not null
            && _cachedForUserKey == _credentials.HasUserKey
            && DateTimeOffset.UtcNow - _cachedAt < CacheDuration;

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        await using var stream = await _http
            .GetStreamAsync("https://openrouter.ai/api/v1/models", ct)
            .ConfigureAwait(false);
        var envelope = await JsonSerializer
            .DeserializeAsync<ModelListEnvelope>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        var models = envelope?.Data?.Select(ParseModel).ToList() ?? [];
        if (!_credentials.HasUserKey)
        {
            models = models
                .Where(model => OpenRouterCredentialService.IsSharedFreeModel(model.Id))
                .ToList();
            if (!models.Any(model =>
                    string.Equals(model.Id, "openrouter/free", StringComparison.OrdinalIgnoreCase)))
            {
                models.Insert(0, FreeRouterModel());
            }
        }
        _cached = models;
        _cachedForUserKey = _credentials.HasUserKey;
        _cachedAt = DateTimeOffset.UtcNow;
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static OpenRouterModel FreeRouterModel()
        => new(
            "openrouter/free",
            "Free Models Router",
            0,
            DateTimeOffset.UnixEpoch,
            "text->text",
            new OpenRouterPricing(0, 0),
            []);

    private static OpenRouterModel ParseModel(JsonElement element)
    {
        var raw = element.Deserialize<RawModel>(JsonOptions)
            ?? throw new InvalidOperationException("Empty model element.");
        return new OpenRouterModel(
            raw.Id ?? string.Empty,
            raw.Name ?? string.Empty,
            raw.ContextLength,
            DateTimeOffset.FromUnixTimeSeconds(raw.Created),
            raw.Architecture?.Modality ?? string.Empty,
            new OpenRouterPricing(
                decimal.Parse(raw.Pricing?.Prompt ?? "0", CultureInfo.InvariantCulture),
                decimal.Parse(raw.Pricing?.Completion ?? "0", CultureInfo.InvariantCulture)),
            raw.SupportedParameters ?? []);
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
        [JsonPropertyName("prompt")] public string? Prompt { get; set; }
        [JsonPropertyName("completion")] public string? Completion { get; set; }
    }
}
