using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Agentic.Chat.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Services;

public sealed class ChatAgentService
{
    private static readonly Action<ILogger, int, Exception?> LogStreamingStart =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            default,
            "Streaming chat completion with {MessageCount} message(s) in transcript");

    internal const string EmptyResponsePlaceholder = "(No response content returned.)";
    private readonly IOpenRouterClient _client;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<ChatAgentService> _logger;
    private readonly SelectedModelService _selectedModelService;
    private readonly ModelCatalogService _modelCatalog;
    private readonly SystemPromptService _systemPromptService;
    private readonly IActiveConversationWriter _conversationWriter;
    private readonly List<ChatDisplayMessage> _displayMessages = [];
    private readonly List<ApiChatMessage> _apiMessages = [];
    private bool _streamActive;

    /// <summary>
    /// Creates the scoped chat agent with a leading system message resolved from
    /// the UI override, then options, then <see cref="OpenRouterOptions.DefaultSystemPrompt"/>.
    /// </summary>
    public ChatAgentService(
        IOpenRouterClient client,
        IOptions<OpenRouterOptions> options,
        ILogger<ChatAgentService> logger,
        SelectedModelService selectedModelService,
        ModelCatalogService modelCatalog,
        SystemPromptService systemPromptService,
        IActiveConversationWriter conversationWriter)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(selectedModelService);
        ArgumentNullException.ThrowIfNull(modelCatalog);
        ArgumentNullException.ThrowIfNull(systemPromptService);
        ArgumentNullException.ThrowIfNull(conversationWriter);

        _client = client;
        _options = options.Value;
        _logger = logger;
        _selectedModelService = selectedModelService;
        _modelCatalog = modelCatalog;
        _systemPromptService = systemPromptService;
        _conversationWriter = conversationWriter;
        _apiMessages.Add(new ApiChatMessage("system", ResolveSystemPrompt(), null));
    }

    public IReadOnlyList<ChatDisplayMessage> Messages => _displayMessages;

    public bool IsStreamActive => _streamActive;

    // Test-only: exposes the API transcript so cancellation/hygiene tests can assert
    // display-only markers (e.g. "(stopped)") never leak into model-visible history.
    // Exposed via InternalsVisibleTo.
    internal IReadOnlyList<ApiChatMessage> ApiMessagesForTest => _apiMessages;

    // Test-only: lets unit tests seed display-list states the public API can't
    // produce on its own (e.g. a trailing user message — normal sends always pair
    // user with an assistant placeholder). Exposed via InternalsVisibleTo.
    internal void AddDisplayMessageForTest(string role, string content)
    {
        _displayMessages.Add(new ChatDisplayMessage { Role = role, Content = content });
    }

    public void LoadTranscript(IReadOnlyList<ChatDisplayMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (_streamActive)
        {
            return;
        }

        _displayMessages.Clear();
        _apiMessages.Clear();
        _apiMessages.Add(new ApiChatMessage("system", ResolveSystemPrompt(), null));

        foreach (var message in messages)
        {
            var copy = new ChatDisplayMessage
            {
                Role = message.Role,
                Content = message.Content,
                Reasoning = message.Reasoning,
                IsStreaming = false,
                IsError = message.IsError
            };
            _displayMessages.Add(copy);

            if (copy.Role == "user")
            {
                _apiMessages.Add(new ApiChatMessage("user", copy.Content, null));
            }
            else if (copy.Role == "assistant"
                && !copy.IsError
                && HasApiVisibleContent(copy.Content, copy.Reasoning))
            {
                _apiMessages.Add(new ApiChatMessage(
                    "assistant",
                    copy.Content,
                    string.IsNullOrWhiteSpace(copy.Reasoning) ? null : copy.Reasoning));
            }
        }
    }

    /// <summary>
    /// Clears the display and API transcripts and reseeds the leading system message
    /// from the current prompt resolution. No-op while a stream is active.
    /// </summary>
    public void Reset()
    {
        if (_streamActive)
        {
            return;
        }

        _displayMessages.Clear();
        _apiMessages.Clear();
        _apiMessages.Add(new ApiChatMessage("system", ResolveSystemPrompt(), null));
    }

    /// <summary>
    /// When the transcript is idle (no display messages), refreshes the system entry so a
    /// newly loaded or applied UI prompt takes effect without mid-conversation surgery.
    /// No-op while streaming or once the user has started a conversation — the next
    /// Reset()/New chat picks up the configured prompt then.
    /// </summary>
    public void RefreshSystemMessageIfIdle()
    {
        if (_streamActive || _displayMessages.Count > 0)
        {
            return;
        }

        _apiMessages.Clear();
        _apiMessages.Add(new ApiChatMessage("system", ResolveSystemPrompt(), null));
    }

    internal string ResolveSystemPrompt()
    {
        if (!string.IsNullOrWhiteSpace(_systemPromptService.CurrentPrompt))
        {
            return _systemPromptService.CurrentPrompt.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
        {
            return _options.SystemPrompt.Trim();
        }

        return OpenRouterOptions.DefaultSystemPrompt;
    }

    // Test-only: first _apiMessages entry must reflect the configured prompt.
    internal string GetApiSystemPromptForTest() => _apiMessages[0].Content;

    // Send a new user turn and stream the assistant response.
    public IAsyncEnumerable<ChatDisplayMessage> SendStreamingAsync(
        string userText,
        CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Send, userText, cancellationToken);

    public IAsyncEnumerable<ChatDisplayMessage> RetryLastAsync(CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Retry, null, cancellationToken);

    public IAsyncEnumerable<ChatDisplayMessage> RegenerateAsync(CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Regenerate, null, cancellationToken);

    private async IAsyncEnumerable<ChatDisplayMessage> StreamTurnAsync(
        TurnKind kind,
        string? userText,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_streamActive)
        {
            throw new InvalidOperationException(
                "A stream is already in progress. Wait for it to finish or cancel it before starting another.");
        }

        _streamActive = true;
        try
        {
            if (kind == TurnKind.Send)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(userText);
                var trimmed = userText!.Trim();
                _displayMessages.Add(new ChatDisplayMessage { Role = "user", Content = trimmed });
                _apiMessages.Add(new ApiChatMessage("user", trimmed, null));

                var modelId = _selectedModelService.CurrentModelId ?? _options.Model;
                await _conversationWriter
                    .OnUserMessageCommittedAsync(trimmed, modelId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (kind == TurnKind.Retry)
            {
                if (!TryPopErrorPlaceholder())
                {
                    yield break;
                }
            }
            else
            {
                if (!TryPopLastCompletedAssistant(out var wasPersisted))
                {
                    yield break;
                }

                if (wasPersisted)
                {
                    await _conversationWriter
                        .OnLastAssistantRemovedAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var assistant = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };
            _displayMessages.Add(assistant);
            yield return assistant;

            LogStreamingStart(_logger, _apiMessages.Count, null);

            var modelIdForRequest = _selectedModelService.CurrentModelId ?? _options.Model;

            // Catalog cache-hit skips its own CT checks, so we ThrowIfCancellationRequested
            // before FindByIdAsync; mid-stream cancel is caught around MoveNextAsync.
            OpenRouterModel? modelInfo = null;
            OperationCanceledException? canceledException = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                modelInfo = await _modelCatalog
                    .FindByIdAsync(modelIdForRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                canceledException = ex;
            }

            if (canceledException is null)
            {
                var request = new ChatCompletionRequest(
                    modelIdForRequest,
                    _apiMessages,
                    Stream: true,
                    Reasoning: modelInfo?.SupportsReasoning == true
                        ? new ReasoningRequest(Enabled: true, Exclude: false)
                        : null);

                OpenRouterException? openRouterException = null;
                var enumerator = _client
                    .StreamChatAsync(request, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                try
                {
                    while (true)
                    {
                        bool hasNext;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        }
                        catch (OpenRouterException ex)
                        {
                            openRouterException = ex;
                            break;
                        }
                        catch (OperationCanceledException ex)
                        {
                            canceledException = ex;
                            break;
                        }

                        if (!hasNext)
                        {
                            break;
                        }

                        var delta = enumerator.Current;
                        if (ApplyDelta(delta, assistant))
                        {
                            yield return assistant;
                        }
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }

                if (openRouterException is not null)
                {
                    assistant.IsStreaming = false;
                    assistant.IsError = true;
                    assistant.Content = $"(Error {openRouterException.StatusCode}: {Truncate(openRouterException.Body, 300)})";
                    yield return assistant;
                    yield break;
                }
            } // end canceledException is null guard

            if (canceledException is not null)
            {
                // Await persistence before rethrowing so Chat.razor's queue drain
                // cannot commit the next user turn ahead of this partial assistant.
                await FinalizeCancelledAssistantAsync(assistant).ConfigureAwait(false);
                yield return assistant;
                // Re-throw so Chat.razor can announce "Response stopped" without an error banner.
                ExceptionDispatchInfo.Capture(canceledException).Throw();
            }

            assistant.IsStreaming = false;

            var hadRealContent = !string.IsNullOrWhiteSpace(assistant.Content)
                || !string.IsNullOrWhiteSpace(assistant.Reasoning);

            if (hadRealContent)
            {
                _apiMessages.Add(new ApiChatMessage(
                    "assistant",
                    assistant.Content,
                    string.IsNullOrWhiteSpace(assistant.Reasoning) ? null : assistant.Reasoning));
            }
            else
            {
                assistant.Content = EmptyResponsePlaceholder;
            }

            await _conversationWriter
                .OnAssistantFinalizedAsync(
                    assistant.Content,
                    string.IsNullOrWhiteSpace(assistant.Reasoning) ? null : assistant.Reasoning,
                    cancellationToken)
                .ConfigureAwait(false);

            yield return assistant;
        }
        finally
        {
            _streamActive = false;
        }
    }

    // User-initiated stop (or navigate-away cancel): keep whatever partial tokens
    // already arrived, append them to the API transcript WITHOUT the display-only
    // "(stopped)" marker, persist partial content to the conversation store,
    // and clear IsStreaming so the UI unlocks cleanly.
    private async Task FinalizeCancelledAssistantAsync(ChatDisplayMessage assistant)
    {
        assistant.IsStreaming = false;

        var apiContent = assistant.Content;
        var apiReasoning = assistant.Reasoning;
        var hadPartial = !string.IsNullOrWhiteSpace(apiContent)
            || !string.IsNullOrWhiteSpace(apiReasoning);
        if (hadPartial)
        {
            _apiMessages.Add(new ApiChatMessage(
                "assistant",
                apiContent,
                string.IsNullOrWhiteSpace(apiReasoning) ? null : apiReasoning));

            // Await with a non-cancellable token so Stop/dispose cancel doesn't
            // drop the partial response, and so queued follow-ups cannot persist
            // a user turn before this assistant row lands.
            await _conversationWriter.OnAssistantFinalizedAsync(
                apiContent,
                string.IsNullOrWhiteSpace(apiReasoning) ? null : apiReasoning,
                CancellationToken.None).ConfigureAwait(false);
        }

        // Display-only marker — MUST NOT enter the API transcript.
        assistant.Content = string.IsNullOrEmpty(apiContent)
            ? "(stopped)"
            : apiContent + " (stopped)";
    }

    internal bool TryPopErrorPlaceholder()
    {
        if (_displayMessages.Count == 0)
        {
            return false;
        }

        var last = _displayMessages[^1];
        if (last.Role != "assistant" || !last.IsError)
        {
            return false;
        }

        _displayMessages.RemoveAt(_displayMessages.Count - 1);
        return true;
    }

    internal bool TryPopLastCompletedAssistant(out bool wasPersisted)
    {
        wasPersisted = false;

        if (_displayMessages.Count == 0)
        {
            return false;
        }

        var last = _displayMessages[^1];
        if (last.Role != "assistant" || last.IsError || last.IsStreaming)
        {
            return false;
        }

        // Determine whether this assistant was persisted to the conversation store.
        // Empty-response placeholders and assistants with no API-visible content are
        // display-only and never saved to SQLite.
        wasPersisted = HasApiVisibleContent(last.Content, last.Reasoning ?? string.Empty);

        _displayMessages.RemoveAt(_displayMessages.Count - 1);
        if (_apiMessages[^1].Role == "assistant")
        {
            _apiMessages.RemoveAt(_apiMessages.Count - 1);
        }

        return true;
    }

    private static bool ApplyDelta(StreamDelta delta, ChatDisplayMessage assistant)
    {
        var changed = false;

        if (!string.IsNullOrEmpty(delta.Reasoning))
        {
            assistant.Reasoning += delta.Reasoning;
            changed = true;
        }

        if (!string.IsNullOrEmpty(delta.Content))
        {
            assistant.Content += delta.Content;
            changed = true;
        }

        return changed;
    }

    internal static bool HasApiVisibleContent(string content, string reasoning)
    {
        if (!string.IsNullOrWhiteSpace(reasoning))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(content)
            && !string.Equals(content, EmptyResponsePlaceholder, StringComparison.Ordinal);
    }

    internal static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    private enum TurnKind
    {
        Send,
        Retry,
        Regenerate
    }
}
