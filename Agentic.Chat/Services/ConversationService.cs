using System.Security.Cryptography;
using System.Text.Json;
using Agentic.Chat.Data;
using Agentic.Chat.Models;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;

namespace Agentic.Chat.Services;

public sealed record ConversationListItem(Guid Id, string Title, DateTimeOffset UpdatedAt);

// Scoped orchestrator for multi-conversation UI (issue #13). Lists/switches/renames/
// deletes conversations and loads the active transcript into ChatAgentService.
// Turn persistence is owned by ConversationPersistence (IActiveConversationWriter).
public sealed class ConversationService
{
    public const string ActiveConversationStorageKey = "active-conversation";
    public const string DefaultTitle = ConversationTitle.Default;
    public const int AutoTitleMaxLength = ConversationTitle.MaxLength;
    private const int TitleMaxLength = 200;

    private readonly ChatDbContext _db;
    private readonly ChatAgentService _chat;
    private readonly ConversationPersistence _persistence;
    private readonly ProtectedLocalStorage _protectedStore;

    public ConversationService(
        ChatDbContext db,
        ChatAgentService chat,
        ConversationPersistence persistence,
        ProtectedLocalStorage protectedStore)
    {
        _db = db;
        _chat = chat;
        _persistence = persistence;
        _protectedStore = protectedStore;
    }

    public Guid? ActiveConversationId => _persistence.ActiveConversationId;

    public bool IsLoaded { get; private set; }

    public IReadOnlyList<ConversationListItem> Conversations { get; private set; } = [];

    public event Action? OnChange;

    public static string AutoTitle(string? firstUserMessage)
        => ConversationTitle.FromFirstUserMessage(firstUserMessage);

    public static string BuildAutoTitle(string? firstUserMessage) => AutoTitle(firstUserMessage);

    public async Task InitializeAsync()
    {
        await RefreshListCoreAsync().ConfigureAwait(false);

        var storedId = await TryReadActiveIdAsync().ConfigureAwait(false);
        if (storedId is Guid id && Conversations.Any(c => c.Id == id))
        {
            await SwitchAsync(id).ConfigureAwait(false);
            return;
        }

        if (Conversations.Count > 0)
        {
            await SwitchAsync(Conversations[0].Id).ConfigureAwait(false);
            return;
        }

        _persistence.ActiveConversationId = null;
        _chat.Reset();
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public Task CreateNewAsync() => NewChatAsync();

    public Task StartNewChatAsync() => NewChatAsync();

    public async Task NewChatAsync()
    {
        if (_chat.IsStreamActive)
        {
            return;
        }

        _persistence.ActiveConversationId = null;
        _chat.Reset();
        await ClearActiveIdAsync().ConfigureAwait(false);
        await RefreshListCoreAsync().ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public async Task SwitchAsync(Guid conversationId)
    {
        if (_chat.IsStreamActive)
        {
            return;
        }

        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return;
        }

        var display = conversation.Messages
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new ChatDisplayMessage
            {
                Role = m.Role,
                Content = m.Content,
                Reasoning = m.Reasoning ?? string.Empty
            })
            .ToList();
        _chat.LoadTranscript(display);
        _persistence.ActiveConversationId = conversation.Id;
        await PersistActiveIdAsync(conversation.Id).ConfigureAwait(false);
        await RefreshListCoreAsync().ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public Task SelectAsync(Guid conversationId) => SwitchAsync(conversationId);

    public async Task RenameAsync(Guid conversationId, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return;
        }

        var trimmed = title.Trim();
        if (trimmed.Length > TitleMaxLength)
        {
            trimmed = trimmed[..TitleMaxLength];
        }

        conversation.Title = trimmed;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync().ConfigureAwait(false);
        await RefreshListAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid conversationId)
    {
        if (_chat.IsStreamActive)
        {
            return;
        }

        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return;
        }

        var wasActive = _persistence.ActiveConversationId == conversationId;
        _db.Conversations.Remove(conversation);
        await _db.SaveChangesAsync().ConfigureAwait(false);
        await RefreshListCoreAsync().ConfigureAwait(false);
        if (!wasActive)
        {
            OnChange?.Invoke();
            return;
        }

        if (Conversations.Count > 0)
        {
            await SwitchAsync(Conversations[0].Id).ConfigureAwait(false);
            return;
        }

        _persistence.ActiveConversationId = null;
        _chat.Reset();
        await ClearActiveIdAsync().ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
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
        if (_persistence.ActiveConversationId is Guid id)
        {
            await PersistActiveIdAsync(id).ConfigureAwait(false);
        }
    }

    public Task RefreshAfterPersistAsync() => RefreshAfterTurnAsync();

    public Task NotifyTurnPersistedAsync() => RefreshAfterTurnAsync();

    public Task SaveProgressAsync() => RefreshAfterTurnAsync();

    private async Task RefreshListCoreAsync()
    {
        // SQLite cannot ORDER BY DateTimeOffset — load then sort in memory.
        var rows = await _db.Conversations
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);
        Conversations = rows
            .OrderByDescending(c => c.UpdatedAt.UtcTicks)
            .Select(c => new ConversationListItem(c.Id, c.Title, c.UpdatedAt))
            .ToList();
    }

    private async Task<Guid?> TryReadActiveIdAsync()
    {
        try
        {
            var stored = await _protectedStore
                .GetAsync<string>(ActiveConversationStorageKey)
                .ConfigureAwait(false);
            if (stored.Success
                && !string.IsNullOrEmpty(stored.Value)
                && Guid.TryParse(stored.Value, out var id))
            {
                return id;
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (CryptographicException)
        {
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private async Task PersistActiveIdAsync(Guid id)
    {
        try
        {
            await _protectedStore
                .SetAsync(ActiveConversationStorageKey, id.ToString("D"))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or JsonException)
        {
        }
    }

    private async Task ClearActiveIdAsync()
    {
        try
        {
            await _protectedStore.DeleteAsync(ActiveConversationStorageKey).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or JsonException)
        {
        }
    }

    internal void SetActiveConversationIdForTest(Guid? id)
    {
        _persistence.ActiveConversationId = id;
        IsLoaded = true;
    }
}

