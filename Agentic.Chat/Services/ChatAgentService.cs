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

    private static readonly Action<ILogger, Exception?> LogCancelPersistFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            default,
            "Failed to persist partial assistant after cancel; continuing with stop UX");

    internal const string EmptyResponsePlaceholder = "(No response content returned.)";
    private readonly IOpenRouterClient _client;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<ChatAgentService> _logger;
    private readonly SelectedModelService _selectedModelService;
    private readonly ModelCatalogService _modelCatalog;
    private readonly SystemPromptService _systemPromptService;
    private readonly ChatSettingsService _chatSettings;
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
        ChatSettingsService chatSettings,
        IActiveConversationWriter conversationWriter)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(selectedModelService);
        ArgumentNullException.ThrowIfNull(modelCatalog);
        ArgumentNullException.ThrowIfNull(systemPromptService);
        ArgumentNullException.ThrowIfNull(chatSettings);
        ArgumentNullException.ThrowIfNull(conversationWriter);

        _client = client;
        _options = options.Value;
        _logger = logger;
        _selectedModelService = selectedModelService;
        _modelCatalog = modelCatalog;
        _systemPromptService = systemPromptService;
        _chatSettings = chatSettings;
        _conversationWriter = conversationWriter;
        ReseedSystemPrompt();
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
        ReseedSystemPrompt();

        foreach (var message in messages)
        {
            var copy = new ChatDisplayMessage
            {
                Role = message.Role,
                Content = message.Content,
                Reasoning = message.Reasoning,
                IsStreaming = false,
                IsError = message.IsError,
                ImageDataUrl = message.ImageDataUrl,
                Usage = message.Usage
            };
            _displayMessages.Add(copy);

            if (copy.Role == "user")
            {
                _apiMessages.Add(string.IsNullOrEmpty(copy.ImageDataUrl)
                    ? new ApiChatMessage("user", copy.Content, null)
                    : ApiChatMessage.UserWithImage(copy.Content, copy.ImageDataUrl));
            }
            else if (copy.Role == "assistant"
                && !copy.IsError
                && HasApiVisibleContent(copy.Content, copy.Reasoning))
            {
                _apiMessages.Add(new ApiChatMessage(
                    "assistant",
                    copy.Content,
                    NullIfWhiteSpace(copy.Reasoning)));
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
        ReseedSystemPrompt();
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

        ReseedSystemPrompt();
    }

    private void ReseedSystemPrompt()
    {
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

    private string ResolveModelId()
        => _selectedModelService.CurrentModelId ?? _options.Model;

    // Test-only: first _apiMessages entry must reflect the configured prompt.
    internal string GetApiSystemPromptForTest() => _apiMessages[0].TextContent;

    // Send a new user turn and stream the assistant response.
    public IAsyncEnumerable<ChatDisplayMessage> SendStreamingAsync(
        string userText,
        string? imageDataUrl = null,
        CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Send, userText, imageDataUrl, cancellationToken);

    public IAsyncEnumerable<ChatDisplayMessage> RetryLastAsync(CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Retry, null, null, cancellationToken);

    public IAsyncEnumerable<ChatDisplayMessage> RegenerateAsync(CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Regenerate, null, null, cancellationToken);

    private async IAsyncEnumerable<ChatDisplayMessage> StreamTurnAsync(
        TurnKind kind,
        string? userText,
        string? imageDataUrl,
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
            if (!await TryPrepareTurnAsync(kind, userText, imageDataUrl, cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }

            var assistant = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };
            _displayMessages.Add(assistant);
            yield return assistant;

            LogStreamingStart(_logger, _apiMessages.Count, null);

            var modelIdForRequest = ResolveModelId();

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
                var request = BuildCompletionRequest(
                    modelIdForRequest,
                    _apiMessages,
                    modelInfo,
                    _chatSettings.ReasoningEffort,
                    _chatSettings.Temperature,
                    _chatSettings.MaxTokens);

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

            FinalizeUsage(assistant, modelInfo);

            // Any non-whitespace content/reasoning counts here — including a literal
            // EmptyResponsePlaceholder string from the model. HasApiVisibleContent
            // excludes that placeholder (persistence/regenerate), so do not reuse it.
            var reasoning = NullIfWhiteSpace(assistant.Reasoning);
            var hadRealContent = !string.IsNullOrWhiteSpace(assistant.Content)
                || !string.IsNullOrWhiteSpace(assistant.Reasoning);

            if (hadRealContent)
            {
                _apiMessages.Add(new ApiChatMessage("assistant", assistant.Content, reasoning));
            }
            else
            {
                assistant.Content = EmptyResponsePlaceholder;
            }

            await _conversationWriter
                .OnAssistantFinalizedAsync(assistant.Content, reasoning, assistant.Usage, cancellationToken)
                .ConfigureAwait(false);

            yield return assistant;
        }
        finally
        {
            _streamActive = false;
        }
    }

    private async Task<bool> TryPrepareTurnAsync(
        TurnKind kind,
        string? userText,
        string? imageDataUrl,
        CancellationToken cancellationToken)
    {
        if (kind == TurnKind.Send)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userText);
            var trimmed = userText!.Trim();
            _displayMessages.Add(new ChatDisplayMessage
            {
                Role = "user",
                Content = trimmed,
                ImageDataUrl = imageDataUrl
            });
            _apiMessages.Add(string.IsNullOrEmpty(imageDataUrl)
                ? new ApiChatMessage("user", trimmed, null)
                : ApiChatMessage.UserWithImage(trimmed, imageDataUrl));

            await _conversationWriter
                .OnUserMessageCommittedAsync(trimmed, ResolveModelId(), imageDataUrl, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (kind == TurnKind.Retry)
        {
            return TryPopErrorPlaceholder();
        }

        if (!TryPopLastCompletedAssistant(out var wasPersisted))
        {
            return false;
        }

        if (wasPersisted)
        {
            await _conversationWriter
                .OnLastAssistantRemovedAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    // User-initiated stop (or navigate-away cancel): keep whatever partial tokens
    // already arrived, append them to the API transcript WITHOUT the display-only
    // "(stopped)" marker, persist partial content to the conversation store,
    // and clear IsStreaming so the UI unlocks cleanly.
    private async Task FinalizeCancelledAssistantAsync(ChatDisplayMessage assistant)
    {
        assistant.IsStreaming = false;

        var apiContent = assistant.Content;
        var reasoning = NullIfWhiteSpace(assistant.Reasoning);
        var hadPartial = !string.IsNullOrWhiteSpace(apiContent)
            || !string.IsNullOrWhiteSpace(assistant.Reasoning);
        if (hadPartial)
        {
            _apiMessages.Add(new ApiChatMessage("assistant", apiContent, reasoning));

            // Await with a non-cancellable token so Stop/dispose cancel doesn't
            // drop the partial response, and so queued follow-ups cannot persist
            // a user turn before this assistant row lands. Persist failures must
            // not replace the OCE — Chat.razor still needs "Response stopped".
            try
            {
                await _conversationWriter.OnAssistantFinalizedAsync(
                    apiContent,
                    reasoning,
                    assistant.Usage,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogCancelPersistFailed(_logger, ex);
            }
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
        wasPersisted = HasApiVisibleContent(last.Content, last.Reasoning);

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

        if (delta.Usage is not null)
        {
            assistant.Usage = delta.Usage;
            changed = true;
        }

        return changed;
    }

    internal static void FinalizeUsage(ChatDisplayMessage assistant, OpenRouterModel? modelInfo)
    {
        if (assistant.Usage is null)
        {
            return;
        }

        var usage = assistant.Usage;
        if (usage.Cost is null && modelInfo is not null)
        {
            var estimated = usage.PromptTokens * modelInfo.Pricing.PromptPerToken
                + usage.CompletionTokens * modelInfo.Pricing.CompletionPerToken;
            usage = usage with { Cost = estimated };
        }

        if (modelInfo is { IsFree: true } && usage.Cost.GetValueOrDefault() == 0m)
        {
            usage = usage with { IsFree = true, Cost = 0m };
        }

        assistant.Usage = usage;
    }

    internal static bool HasApiVisibleContent(string content, string? reasoning)
    {
        if (!string.IsNullOrWhiteSpace(reasoning))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(content)
            && !string.Equals(content, EmptyResponsePlaceholder, StringComparison.Ordinal);
    }

    // Visible to tests: shared null-vs-whitespace normalization for API reasoning fields.
    internal static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Shapes the OpenRouter chat/completions body from model capabilities + UI settings.
    /// Effort Off (or non-reasoning models) omits <c>reasoning</c>; temperature / max_tokens
    /// are sent only when the catalog lists them in <see cref="OpenRouterModel.SupportedParameters"/>.
    /// </summary>
    internal static ChatCompletionRequest BuildCompletionRequest(
        string modelId,
        IReadOnlyList<ApiChatMessage> messages,
        OpenRouterModel? modelInfo,
        ReasoningEffortLevel effort,
        double? temperature,
        int? maxTokens)
    {
        ReasoningRequest? reasoning = null;
        if (modelInfo?.SupportsReasoning == true && effort != ReasoningEffortLevel.Off)
        {
            reasoning = new ReasoningRequest(
                Effort: EffortToApiString(effort),
                Exclude: false);
        }

        double? sendTemperature = null;
        if (temperature is not null
            && modelInfo is not null
            && SupportsParameter(modelInfo, "temperature"))
        {
            sendTemperature = temperature;
        }

        int? sendMaxTokens = null;
        if (maxTokens is not null
            && modelInfo is not null
            && SupportsParameter(modelInfo, "max_tokens"))
        {
            sendMaxTokens = maxTokens;
        }

        return new ChatCompletionRequest(
            modelId,
            messages,
            Stream: true,
            Reasoning: reasoning,
            Temperature: sendTemperature,
            MaxTokens: sendMaxTokens);
    }

    private static string EffortToApiString(ReasoningEffortLevel effort) => effort switch
    {
        ReasoningEffortLevel.Low => "low",
        ReasoningEffortLevel.High => "high",
        _ => "medium"
    };

    private static bool SupportsParameter(OpenRouterModel model, string name)
        => model.SupportedParameters.Contains(name, StringComparer.OrdinalIgnoreCase);

    internal static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    private enum TurnKind
    {
        Send,
        Retry,
        Regenerate
    }
}
