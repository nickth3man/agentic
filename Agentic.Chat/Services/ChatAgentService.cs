using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Agentic.Chat.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Services;

public sealed class ChatAgentService
{
    // Precompiled logging delegate (CA1848/CA1873): when Information logging is
    // off, this skips the params-array allocation and the template formatting
    // that an inline _logger.LogInformation(...) call does unconditionally. (The
    // argument expression itself — _apiMessages.Count — is still evaluated.)
    private static readonly Action<ILogger, int, Exception?> LogStreamingStart =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            default,
            "Streaming chat completion with {MessageCount} message(s) in transcript");

    private const string SystemPrompt = "You are a helpful chat agent.";

    private readonly IOpenRouterClient _client;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<ChatAgentService> _logger;
    private readonly SelectedModelService _selectedModelService;
    private readonly ModelCatalogService _modelCatalog;
    private readonly List<ChatDisplayMessage> _displayMessages = [];
    private readonly List<ApiChatMessage> _apiMessages =
    [
        new ApiChatMessage("system", SystemPrompt, null)
    ];
    private bool _streamActive;

    public ChatAgentService(
        IOpenRouterClient client,
        IOptions<OpenRouterOptions> options,
        ILogger<ChatAgentService> logger,
        SelectedModelService selectedModelService,
        ModelCatalogService modelCatalog)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _selectedModelService = selectedModelService;
        _modelCatalog = modelCatalog;
    }

    public IReadOnlyList<ChatDisplayMessage> Messages => _displayMessages;

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

    public void Reset()
    {
        if (_streamActive)
        {
            return;
        }

        _displayMessages.Clear();
        _apiMessages.Clear();
        _apiMessages.Add(new ApiChatMessage("system", SystemPrompt, null));
    }

    // Send a new user turn and stream the assistant response.
    public IAsyncEnumerable<ChatDisplayMessage> SendStreamingAsync(
        string userText,
        CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Send, userText, cancellationToken);

    // Retry the most recent failed turn: drop the error placeholder and re-stream the
    // assistant for the last user turn (already in the transcript — not re-added, so a
    // retry never compounds the user message). No-op (empty stream) if the last message
    // isn't an error assistant.
    public IAsyncEnumerable<ChatDisplayMessage> RetryLastAsync(CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Retry, null, cancellationToken);

    // Regenerate the last completed assistant turn: pop it from the display list and the
    // API transcript, then re-stream. No-op (empty stream) if the last message isn't a
    // completed (non-error, non-streaming) assistant.
    public IAsyncEnumerable<ChatDisplayMessage> RegenerateAsync(CancellationToken cancellationToken = default)
        => StreamTurnAsync(TurnKind.Regenerate, null, cancellationToken);

    // The single async iterator backing all three public entry points. Parameterizing on
    // TurnKind (rather than consuming a nested helper iterator) keeps this as ONE state
    // machine — nested async iterators generate compiler branches that can't reach 100%
    // branch coverage. Validation for Send runs on first enumeration (matching the prior
    // async-iterator semantics); Retry/Regenerate pop their target eagerly here and
    // yield-break (no-op) when the precondition fails.
    //
    // C# forbids yield inside a try that has a catch, so MoveNextAsync is advanced
    // manually and OpenRouterException is captured into a local — the outer try has only
    // a finally so yield is permitted.
    //
    // Transcript invariant: the assistant entry is appended to _apiMessages ONLY when the
    // stream completed cleanly AND produced real content/reasoning, OR when the user
    // cancelled mid-stream and partial content/reasoning already arrived. The "(No
    // response content returned.)" placeholder, "(stopped)" display marker, and error
    // states never enter the API transcript.
    private async IAsyncEnumerable<ChatDisplayMessage> StreamTurnAsync(
        TurnKind kind,
        string? userText,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _streamActive = true;
        try
        {
            if (kind == TurnKind.Send)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(userText);
                var trimmed = userText!.Trim();
                _displayMessages.Add(new ChatDisplayMessage { Role = "user", Content = trimmed });
                _apiMessages.Add(new ApiChatMessage("user", trimmed, null));
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
                if (!TryPopLastCompletedAssistant())
                {
                    yield break;
                }
            }

            var assistant = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };
            _displayMessages.Add(assistant);
            yield return assistant; // placeholder, before any traffic

            LogStreamingStart(_logger, _apiMessages.Count, null);

            var modelId = _selectedModelService.CurrentModelId ?? _options.Model;

            // Capture cancel/error into locals — C# forbids yield inside a try that has a catch.
            // Catalog cache-hit skips its own CT checks, so we ThrowIfCancellationRequested
            // before FindByIdAsync; mid-stream cancel is caught around MoveNextAsync.
            OpenRouterException? openRouterException = null;
            OperationCanceledException? canceledException = null;
            OpenRouterModel? modelInfo = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                modelInfo = await _modelCatalog
                    .FindByIdAsync(modelId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                canceledException = ex;
            }

            if (canceledException is null)
            {
                var request = new ChatCompletionRequest(
                    modelId,
                    _apiMessages,
                    Stream: true,
                    Reasoning: modelInfo?.SupportsReasoning == true
                        ? new ReasoningRequest(Enabled: true, Exclude: false)
                        : null);

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
            }

            if (canceledException is not null)
            {
                FinalizeCancelledAssistant(assistant);
                yield return assistant;
                // Re-throw so Chat.razor can announce "Response stopped" without an error banner.
                ExceptionDispatchInfo.Capture(canceledException).Throw();
            }

            if (openRouterException is not null)
            {
                assistant.IsStreaming = false;
                assistant.IsError = true;
                assistant.Content = $"(Error {openRouterException.StatusCode}: {Truncate(openRouterException.Body, 300)})";
                yield return assistant;
                yield break;
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
                // Display-only placeholder — MUST NOT enter the API transcript.
                assistant.Content = "(No response content returned.)";
            }

            yield return assistant;
        }
        finally
        {
            _streamActive = false;
        }
    }

    // User-initiated stop (or navigate-away cancel): keep whatever partial tokens
    // already arrived, append them to the API transcript WITHOUT the display-only
    // "(stopped)" marker, and clear IsStreaming so the UI unlocks cleanly.
    private void FinalizeCancelledAssistant(ChatDisplayMessage assistant)
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
        }

        // Display-only marker — MUST NOT enter the API transcript.
        assistant.Content = string.IsNullOrEmpty(apiContent)
            ? "(stopped)"
            : apiContent + " (stopped)";
    }

    // Pops the trailing error assistant from the display list, if any. Used by the Retry
    // path. Does NOT touch _apiMessages — error placeholders were never appended.
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

    // Pops the trailing completed (non-error, non-streaming) assistant from the display
    // list and, when present, the matching assistant entry from the API transcript. Used
    // by the Regenerate path. The transcript-pop is guarded on Role == "assistant": a
    // completed assistant that was an empty-response placeholder has NO transcript entry
    // (the last transcript entry is its user turn), so the guard preserves that user turn.
    internal bool TryPopLastCompletedAssistant()
    {
        if (_displayMessages.Count == 0)
        {
            return false;
        }

        var last = _displayMessages[^1];
        if (last.Role != "assistant" || last.IsError || last.IsStreaming)
        {
            return false;
        }

        _displayMessages.RemoveAt(_displayMessages.Count - 1);
        // _apiMessages always holds at least the system message, so [^1] is safe.
        // Guard on Role == "assistant": a completed assistant that was an empty-response
        // placeholder has NO transcript entry (the last entry is its user turn), so we
        // only pop when the last transcript entry really is the assistant turn.
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

    internal static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    // Discriminates the three streaming entry points inside the single shared iterator.
    private enum TurnKind
    {
        Send,
        Retry,
        Regenerate
    }
}
