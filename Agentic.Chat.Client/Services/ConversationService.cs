using Agentic.Chat.Models;

namespace Agentic.Chat.Services;

public sealed record ConversationListItem(Guid Id, string Title, DateTimeOffset UpdatedAt);

public sealed class ConversationService(
    BrowserConversationStore store,
    ChatAgentService chat,
    ConversationPersistence persistence,
    BrowserStorage storage)
{
    private const string ActiveConversationStorageKey = "active-conversation";
    private const int TitleMaxLength = 200;
    private readonly BrowserConversationStore _store = store;
    private readonly ChatAgentService _chat = chat;
    private readonly ConversationPersistence _persistence = persistence;
    private readonly BrowserStorage _storage = storage;

    public Guid? ActiveConversationId => _persistence.ActiveConversationId;
    public bool IsLoaded { get; private set; }
    public IReadOnlyList<ConversationListItem> Conversations { get; private set; } = [];
    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        if (IsLoaded) { return; }
        await RefreshListCoreAsync().ConfigureAwait(false);
        var storedId = await _storage
            .GetLocalAsync<string>(ActiveConversationStorageKey)
            .ConfigureAwait(false);
        if (Guid.TryParse(storedId, out var id) && Conversations.Any(item => item.Id == id))
        {
            await SwitchAsync(id).ConfigureAwait(false);
        }
        else if (Conversations.Count > 0)
        {
            await SwitchAsync(Conversations[0].Id).ConfigureAwait(false);
        }
        else
        {
            _chat.Reset();
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    public async Task NewChatAsync()
    {
        if (_chat.IsStreamActive) { return; }
        _persistence.ActiveConversationId = null;
        _chat.Reset();
        await _storage.RemoveLocalAsync(ActiveConversationStorageKey).ConfigureAwait(false);
        await RefreshListAsync().ConfigureAwait(false);
    }

    public async Task SwitchAsync(Guid conversationId)
    {
        if (_chat.IsStreamActive) { return; }
        var conversation = await _store.GetAsync(conversationId).ConfigureAwait(false);
        if (conversation is null) { return; }
        _chat.LoadTranscript(conversation.Messages
            .OrderBy(message => message.CreatedAt)
            .Select(message => new ChatDisplayMessage
            {
                Role = message.Role,
                Content = message.Content,
                Reasoning = message.Reasoning ?? string.Empty,
                ImageDataUrl = message.ImageDataUrl,
                Usage = MessageUsage.FromStored(
                    message.UsagePromptTokens,
                    message.UsageCompletionTokens,
                    message.UsageCost,
                    message.UsageIsFree)
            })
            .ToList());
        _persistence.ActiveConversationId = conversationId;
        await _storage
            .SetLocalAsync(ActiveConversationStorageKey, conversationId.ToString("D"))
            .ConfigureAwait(false);
        await RefreshListCoreAsync().ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public async Task RenameAsync(Guid conversationId, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) { return; }
        var conversation = await _store.GetAsync(conversationId).ConfigureAwait(false);
        if (conversation is null) { return; }
        conversation.Title = title.Trim()[..Math.Min(title.Trim().Length, TitleMaxLength)];
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.PutAsync(conversation).ConfigureAwait(false);
        await RefreshListAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid conversationId)
    {
        if (_chat.IsStreamActive) { return; }
        var wasActive = ActiveConversationId == conversationId;
        await _store.DeleteAsync(conversationId).ConfigureAwait(false);
        await RefreshListCoreAsync().ConfigureAwait(false);
        if (!wasActive)
        {
            OnChange?.Invoke();
        }
        else if (Conversations.Count > 0)
        {
            await SwitchAsync(Conversations[0].Id).ConfigureAwait(false);
        }
        else
        {
            await NewChatAsync().ConfigureAwait(false);
        }
    }

    public async Task RefreshListAsync()
    {
        await RefreshListCoreAsync().ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public async Task RefreshAfterTurnAsync()
    {
        await RefreshListAsync().ConfigureAwait(false);
        if (ActiveConversationId is { } id)
        {
            await _storage
                .SetLocalAsync(ActiveConversationStorageKey, id.ToString("D"))
                .ConfigureAwait(false);
        }
    }

    private async Task RefreshListCoreAsync()
    {
        Conversations = (await _store.ListAsync().ConfigureAwait(false))
            .Select(conversation => new ConversationListItem(
                Guid.Parse(conversation.Id),
                conversation.Title,
                conversation.UpdatedAt))
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();
    }
}
