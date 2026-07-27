using Agentic.Chat.Models;

namespace Agentic.Chat.Services;

public interface IActiveConversationWriter
{
    Task OnUserMessageCommittedAsync(
        string content,
        string modelId,
        string? imageDataUrl = null,
        CancellationToken cancellationToken = default);

    Task OnAssistantFinalizedAsync(
        string content,
        string? reasoning,
        MessageUsage? usage = null,
        CancellationToken cancellationToken = default);

    Task OnLastAssistantRemovedAsync(CancellationToken cancellationToken = default);
}

public sealed class ConversationPersistence(BrowserConversationStore store)
    : IActiveConversationWriter
{
    private readonly BrowserConversationStore _store = store;

    public Guid? ActiveConversationId { get; set; }

    public async Task OnUserMessageCommittedAsync(
        string content,
        string modelId,
        string? imageDataUrl = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var conversation = ActiveConversationId is { } activeId
            ? await _store.GetAsync(activeId).ConfigureAwait(false)
            : null;
        if (conversation is null)
        {
            var id = Guid.NewGuid();
            conversation = new StoredConversation
            {
                Id = id.ToString("D"),
                Title = ConversationTitle.FromFirstUserMessage(content),
                Model = modelId,
                CreatedAt = now,
                UpdatedAt = now
            };
            ActiveConversationId = id;
        }

        conversation.Model = modelId;
        conversation.UpdatedAt = now;
        conversation.Messages.Add(new StoredMessage
        {
            Id = Guid.NewGuid().ToString("D"),
            Role = "user",
            Content = content,
            ImageDataUrl = imageDataUrl,
            CreatedAt = now
        });
        await _store.PutAsync(conversation).ConfigureAwait(false);
    }

    public async Task OnAssistantFinalizedAsync(
        string content,
        string? reasoning,
        MessageUsage? usage = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ActiveConversationId is not { } id
            || !ChatAgentService.HasApiVisibleContent(content, reasoning))
        {
            return;
        }
        var conversation = await _store.GetAsync(id).ConfigureAwait(false);
        if (conversation is null) { return; }
        var now = DateTimeOffset.UtcNow;
        conversation.UpdatedAt = now;
        conversation.Messages.Add(new StoredMessage
        {
            Id = Guid.NewGuid().ToString("D"),
            Role = "assistant",
            Content = content,
            Reasoning = ChatAgentService.NullIfWhiteSpace(reasoning),
            UsagePromptTokens = usage?.PromptTokens,
            UsageCompletionTokens = usage?.CompletionTokens,
            UsageCost = usage?.Cost,
            UsageIsFree = usage?.IsFree ?? false,
            CreatedAt = now
        });
        await _store.PutAsync(conversation).ConfigureAwait(false);
    }

    public async Task OnLastAssistantRemovedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ActiveConversationId is not { } id) { return; }
        var conversation = await _store.GetAsync(id).ConfigureAwait(false);
        if (conversation is null) { return; }
        var index = conversation.Messages.FindLastIndex(message => message.Role == "assistant");
        if (index < 0) { return; }
        conversation.Messages.RemoveAt(index);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.PutAsync(conversation).ConfigureAwait(false);
    }
}
