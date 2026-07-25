using Agentic.Chat.Data;
using Microsoft.EntityFrameworkCore;

namespace Agentic.Chat.Services;

public interface IActiveConversationWriter
{
    Task OnUserMessageCommittedAsync(string content, string modelId, CancellationToken cancellationToken = default);
    Task OnAssistantFinalizedAsync(string content, string? reasoning, CancellationToken cancellationToken = default);
    Task OnLastAssistantRemovedAsync(CancellationToken cancellationToken = default);
}

public sealed class NullActiveConversationWriter : IActiveConversationWriter
{
    public static NullActiveConversationWriter Instance { get; } = new();
    public Task OnUserMessageCommittedAsync(string content, string modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OnAssistantFinalizedAsync(string content, string? reasoning, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OnLastAssistantRemovedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class ConversationPersistence(ChatDbContext db) : IActiveConversationWriter
{
    private readonly ChatDbContext _db = db;
    public Guid? ActiveConversationId { get; set; }

    public async Task OnUserMessageCommittedAsync(string content, string modelId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var now = DateTimeOffset.UtcNow;
        Conversation conversation;
        if (ActiveConversationId is null)
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Title = ConversationTitle.FromFirstUserMessage(content),
                Model = modelId,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Conversations.Add(conversation);
        }
        else
        {
            conversation = await _db.Conversations
                .SingleAsync(c => c.Id == ActiveConversationId.Value, cancellationToken)
                .ConfigureAwait(false);
            conversation.UpdatedAt = now;
            conversation.Model = modelId;
            if (string.Equals(conversation.Title, ConversationTitle.Default, StringComparison.Ordinal))
            {
                conversation.Title = ConversationTitle.FromFirstUserMessage(content);
            }
        }

        _db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = "user",
            Content = content,
            Reasoning = null,
            CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Set ActiveConversationId only after the initial save succeeds,
        // so a cancelled or failed first save doesn't leave the circuit
        // stuck on an ID that was never persisted.
        if (ActiveConversationId is null)
        {
            ActiveConversationId = conversation.Id;
        }
    }

    public async Task OnAssistantFinalizedAsync(string content, string? reasoning, CancellationToken cancellationToken = default)
    {
        if (ActiveConversationId is null) { return; }
        if (string.Equals(content, ChatAgentService.EmptyResponsePlaceholder, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(reasoning))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(reasoning)) { return; }

        var now = DateTimeOffset.UtcNow;
        var conversation = await _db.Conversations
            .SingleAsync(c => c.Id == ActiveConversationId.Value, cancellationToken)
            .ConfigureAwait(false);
        conversation.UpdatedAt = now;
        _db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = content ?? string.Empty,
            Reasoning = string.IsNullOrWhiteSpace(reasoning) ? null : reasoning,
            CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task OnLastAssistantRemovedAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveConversationId is null) { return; }
        var assistants = await _db.Messages
            .Where(m => m.ConversationId == ActiveConversationId.Value && m.Role == "assistant")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var lastAssistant = assistants
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefault();
        if (lastAssistant is null) { return; }
        _db.Messages.Remove(lastAssistant);
        var conversation = await _db.Conversations
            .SingleAsync(c => c.Id == ActiveConversationId.Value, cancellationToken)
            .ConfigureAwait(false);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
