using Microsoft.JSInterop;

namespace Agentic.Chat.Services;

public sealed class BrowserConversationStore(IJSRuntime js) : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _module = new(
        () => js.InvokeAsync<IJSObjectReference>(
            "import",
            "./conversation-store.js").AsTask());

    public async Task<IReadOnlyList<StoredConversation>> ListAsync()
    {
        var module = await _module.Value.ConfigureAwait(false);
        return await module
            .InvokeAsync<StoredConversation[]>("list")
            .ConfigureAwait(false);
    }

    public async Task<StoredConversation?> GetAsync(Guid id)
    {
        var module = await _module.Value.ConfigureAwait(false);
        return await module
            .InvokeAsync<StoredConversation?>("get", id.ToString("D"))
            .ConfigureAwait(false);
    }

    public async Task PutAsync(StoredConversation conversation)
    {
        var module = await _module.Value.ConfigureAwait(false);
        await module.InvokeVoidAsync("put", conversation).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id)
    {
        var module = await _module.Value.ConfigureAwait(false);
        await module.InvokeVoidAsync("remove", id.ToString("D")).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module.IsValueCreated)
        {
            var module = await _module.Value.ConfigureAwait(false);
            await module.DisposeAsync().ConfigureAwait(false);
        }
        GC.SuppressFinalize(this);
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
