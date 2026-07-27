using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Agentic.Chat.Services;

public sealed class OpenRouterCredentialService(HttpClient http, BrowserStorage storage)
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

    public bool IsReady { get; private set; }

    public event Action? OnChange;

    public Task InitializeAsync()
        => _initialization ??= InitializeCoreAsync();

    public async Task<string> GetKeyForModelAsync(string modelId)
    {
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

    public static bool IsSharedFreeModel(string modelId)
        => string.Equals(modelId, "openrouter/free", StringComparison.OrdinalIgnoreCase)
            || modelId.EndsWith(":free", StringComparison.OrdinalIgnoreCase);

    private async Task InitializeCoreAsync()
    {
        try
        {
            _userKey = await _storage.GetSessionAsync<string>(SessionUserKey).ConfigureAwait(false)
                ?? await _storage.GetLocalAsync<string>(PersistentUserKey).ConfigureAwait(false);

            try
            {
                var config = await _http
                    .GetFromJsonAsync<ClientAppConfig>("app-config.json")
                    .ConfigureAwait(false);
                _sharedFreeKey = NullIfWhiteSpace(config?.OpenRouterFreeApiKey);
            }
            catch (Exception exception)
                when (exception is HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
            {
                // Local development intentionally works without a shared key.
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

    private static async Task ValidateAsync(string apiKey)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenRouter rejected this API key ({(int)response.StatusCode}).");
        }
    }

    private sealed record ClientAppConfig(string? OpenRouterFreeApiKey);
}
