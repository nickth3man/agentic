using Microsoft.JSInterop;

namespace Agentic.Chat.Services;

public sealed class BrowserConversationStore(IJSRuntime js) : IAsyncDisposable
{
    private readonly object _moduleLock = new();
    private Task<IJSObjectReference>? _moduleTask;

    public async Task<IReadOnlyList<StoredConversation>> ListAsync()
    {
        var module = await GetModuleAsync().ConfigureAwait(false);
        return await module
            .InvokeAsync<StoredConversation[]>("list")
            .ConfigureAwait(false);
    }

    public async Task<StoredConversation?> GetAsync(Guid id)
    {
        var module = await GetModuleAsync().ConfigureAwait(false);
        return await module
            .InvokeAsync<StoredConversation?>("get", id.ToString("D"))
            .ConfigureAwait(false);
    }

    public async Task PutAsync(StoredConversation conversation)
    {
        var module = await GetModuleAsync().ConfigureAwait(false);
        await module.InvokeVoidAsync("put", conversation).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id)
    {
        var module = await GetModuleAsync().ConfigureAwait(false);
        await module.InvokeVoidAsync("remove", id.ToString("D")).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task<IJSObjectReference>? moduleTask;
        lock (_moduleLock)
        {
            moduleTask = _moduleTask;
            _moduleTask = null;
        }

        if (moduleTask is not null)
        {
            var module = await moduleTask.ConfigureAwait(false);
            await module.DisposeAsync().ConfigureAwait(false);
        }
        GC.SuppressFinalize(this);
    }

    private Task<IJSObjectReference> GetModuleAsync()
    {
        lock (_moduleLock)
        {
            if (_moduleTask is null || _moduleTask.IsFaulted || _moduleTask.IsCanceled)
            {
                _moduleTask = js
                    .InvokeAsync<IJSObjectReference>("import", "./conversation-store.js")
                    .AsTask();
            }

            return _moduleTask;
        }
    }
}

public sealed class StoredConversation
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = ConversationTitle.Default;
    public string Model { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<StoredMessage> Messages { get; set; } = [];
}

public sealed class StoredMessage
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Reasoning { get; set; }
    public string? ImageDataUrl { get; set; }
    public int? UsagePromptTokens { get; set; }
    public int? UsageCompletionTokens { get; set; }
    public decimal? UsageCost { get; set; }
    public bool UsageIsFree { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
