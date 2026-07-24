using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<ChatDisplayMessage> SendStreamingAsync(
        string userText,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);

        _streamActive = true;
        try
        {
            var trimmed = userText.Trim();
            _displayMessages.Add(new ChatDisplayMessage { Role = "user", Content = trimmed });
            _apiMessages.Add(new ApiChatMessage("user", trimmed, null));

            var assistant = new ChatDisplayMessage
            {
                Role = "assistant",
                IsStreaming = true
            };
            _displayMessages.Add(assistant);
            yield return assistant;

            LogStreamingStart(_logger, _apiMessages.Count, null);

            var modelId = _selectedModelService.CurrentModelId ?? _options.Model;
            var modelInfo = await _modelCatalog
                .FindByIdAsync(modelId, cancellationToken)
                .ConfigureAwait(false);

            var request = new ChatCompletionRequest(
                modelId,
                _apiMessages,
                Stream: true,
                Reasoning: modelInfo?.SupportsReasoning == true
                    ? new ReasoningRequest(Enabled: true, Exclude: false)
                    : null);

            var completed = false;
            OpenRouterException? openRouterException = null;
            // C# does not permit yield returns inside a try block with a catch clause.
            // Advance the client enumerator manually so exception handling preserves
            // per-delta yields and the completed-flag finalization behavior.
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

                    if (!hasNext)
                    {
                        completed = true;
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
                assistant.Content = $"(Error {openRouterException.StatusCode}: {Truncate(openRouterException.Body, 300)})";
                yield return assistant;
            }

            if (completed)
            {
                assistant.IsStreaming = false;

                if (string.IsNullOrWhiteSpace(assistant.Content) &&
                    string.IsNullOrWhiteSpace(assistant.Reasoning))
                {
                    assistant.Content = "(No response content returned.)";
                }

                // Keep assistant content (+ reasoning when present) in the API transcript for multi-turn continuity.
                _apiMessages.Add(new ApiChatMessage(
                    "assistant",
                    assistant.Content,
                    string.IsNullOrWhiteSpace(assistant.Reasoning) ? null : assistant.Reasoning));

                yield return assistant;
            }
        }
        finally
        {
            _streamActive = false;
        }
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
}
