using System.Threading;
using System.Threading.Tasks;

namespace Agentic.Chat.Services.MultiAgent;

public interface ILocalLlmClient
{
    Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
