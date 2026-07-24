using Agentic.Chat.Models;

namespace Agentic.Chat.Services;

// Owns the OpenRouter chat/completions wire protocol: request serialization, the SSE
// HTTP call, line filtering, and delta decoding. ChatAgentService consumes this.
public interface IOpenRouterClient
{
    IAsyncEnumerable<StreamDelta> StreamChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
