using System.Threading;
using System.Threading.Tasks;

namespace Agentic.Chat.Services.MultiAgent;

public sealed class UnavailableLocalLlm : ILocalLlmClient
{
    public Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        => Task.FromResult("[Local LLM not configured — set MultiAgent.OllamaBaseUrl in app-config.json]");
}
