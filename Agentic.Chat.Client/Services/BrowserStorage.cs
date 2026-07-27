using System.Text.Json;
using Microsoft.JSInterop;

namespace Agentic.Chat.Services;

public sealed class BrowserStorage(IJSRuntime js)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IJSRuntime _js = js;

    public async Task<T?> GetLocalAsync<T>(string key)
        => await GetAsync<T>("localStorage.getItem", key).ConfigureAwait(false);

    public async Task SetLocalAsync<T>(string key, T value)
        => await SetAsync("localStorage.setItem", key, value).ConfigureAwait(false);

    public async Task RemoveLocalAsync(string key)
        => await _js.InvokeVoidAsync("localStorage.removeItem", key).ConfigureAwait(false);

    public async Task<T?> GetSessionAsync<T>(string key)
        => await GetAsync<T>("sessionStorage.getItem", key).ConfigureAwait(false);

    public async Task SetSessionAsync<T>(string key, T value)
        => await SetAsync("sessionStorage.setItem", key, value).ConfigureAwait(false);

    public async Task RemoveSessionAsync(string key)
        => await _js.InvokeVoidAsync("sessionStorage.removeItem", key).ConfigureAwait(false);

    private async Task<T?> GetAsync<T>(string identifier, string key)
    {
        var json = await _js.InvokeAsync<string?>(identifier, key).ConfigureAwait(false);
        return string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private async Task SetAsync<T>(string identifier, string key, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await _js.InvokeVoidAsync(identifier, key, json).ConfigureAwait(false);
    }
}
