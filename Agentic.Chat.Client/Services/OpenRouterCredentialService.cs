using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Agentic.Chat.Services;

public sealed class OpenRouterCredentialService(
    HttpClient http,
    BrowserStorage storage,
    ILogger<OpenRouterCredentialService> logger)
{
    private const string PersistentUserKey = "openrouter-user-api-key";
    private const string SessionUserKey = "openrouter-session-api-key";
    private readonly HttpClient _http = http;
    private readonly BrowserStorage _storage = storage;
    private Task? _initialization;
    private string? _sharedFreeKey;
    private string? _userKey;

    public bool HasUserKey => !string.IsNullOrWhiteSpace(_userKey);

    public bool HasSharedFreeKey => !string.IsNullOrWhiteSpace(_sharedFreeKey);

    public string? SharedConfigurationError { get; private set; }

    public bool IsReady { get; private set; }

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        var initialization = _initialization ??= InitializeCoreAsync();
        try
        {
            await initialization.ConfigureAwait(false);
        }
        catch
        {
            if (ReferenceEquals(_initialization, initialization))
            {
                _initialization = null;
            }
            throw;
        }
    }

    public async Task<string> GetKeyForModelAsync(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        await InitializeAsync().ConfigureAwait(false);
        if (HasUserKey)
        {
            return _userKey!;
        }

        if (!IsSharedFreeModel(modelId))
        {
            throw new InvalidOperationException(
                "This model requires your own OpenRouter API key. Open the key settings to connect one.");
        }

        return _sharedFreeKey
            ?? throw new InvalidOperationException(
                "The shared free-model key is not configured. Add your own OpenRouter API key to continue.");
    }

    /// <summary>
    /// Returns the visitor's own OpenRouter key, awaiting persistence/initialization
    /// first. Throws when no user key has been entered (e.g. on a fresh GitHub Pages
    /// visit). Intended for callers that must never fall back to the static
    /// <c>app-config.json</c> shared free-model key.
    /// </summary>
    public async Task<string> GetUserKeyOrThrowAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        if (HasUserKey)
        {
            return _userKey!;
        }
        throw new InvalidOperationException(
            "OpenRouter multi-agent research requires your own API key. Open the key settings to connect one.");
    }

    public async Task SetUserKeyAsync(string apiKey, bool rememberOnDevice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var trimmed = apiKey.Trim();
        await ValidateAsync(trimmed).ConfigureAwait(false);

        if (rememberOnDevice)
        {
            await _storage.SetLocalAsync(PersistentUserKey, trimmed).ConfigureAwait(false);
            await _storage.RemoveSessionAsync(SessionUserKey).ConfigureAwait(false);
        }
        else
        {
            await _storage.SetSessionAsync(SessionUserKey, trimmed).ConfigureAwait(false);
            await _storage.RemoveLocalAsync(PersistentUserKey).ConfigureAwait(false);
        }

        _userKey = trimmed;
        OnChange?.Invoke();
    }

    public async Task ClearUserKeyAsync()
    {
        await Task.WhenAll(
            _storage.RemoveLocalAsync(PersistentUserKey),
            _storage.RemoveSessionAsync(SessionUserKey)).ConfigureAwait(false);
        _userKey = null;
        OnChange?.Invoke();
    }

    public static bool IsSharedFreeModel(string? modelId)
        => !string.IsNullOrWhiteSpace(modelId)
            && (string.Equals(modelId, "openrouter/free", StringComparison.OrdinalIgnoreCase)
                || modelId.EndsWith(":free", StringComparison.OrdinalIgnoreCase));

    private async Task InitializeCoreAsync()
    {
        try
        {
            try
            {
                _userKey = await _storage.GetSessionAsync<string>(SessionUserKey).ConfigureAwait(false)
                    ?? await _storage.GetLocalAsync<string>(PersistentUserKey).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _userKey = null;
                ClientLog.Warning(
                    logger,
                    exception,
                    "Browser storage is unavailable for the OpenRouter user key.");
            }

            try
            {
                var config = await _http
                    .GetFromJsonAsync<ClientAppConfig>("app-config.json")
                    .ConfigureAwait(false);
                _sharedFreeKey = NullIfWhiteSpace(config?.OpenRouterFreeApiKey);
                SharedConfigurationError = null;
            }
            catch (Exception exception)
                when (exception is HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
            {
                _sharedFreeKey = null;
                SharedConfigurationError = "The shared free-key configuration could not be loaded.";
                ClientLog.Warning(logger, exception, "Could not load app-config.json.");
            }
        }
        finally
        {
            IsReady = true;
            OnChange?.Invoke();
        }
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task ValidateAsync(string apiKey)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await _http
            .SendAsync(request, timeout.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenRouter rejected this API key ({(int)response.StatusCode}).");
        }
    }

    private sealed record ClientAppConfig(string? OpenRouterFreeApiKey);
}
