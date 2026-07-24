namespace Agentic.Chat.Services;

// Thrown by OpenRouterClient when chat/completions returns a non-success status.
// Carries the raw error body so ChatAgentService can format + truncate it for display
// (preserving the prior "(Error {code}: {body})" message).
public sealed class OpenRouterException : Exception
{
    public int StatusCode { get; }
    public string Body { get; }

    public OpenRouterException(int statusCode, string body)
        : base($"OpenRouter chat/completions request failed with status {statusCode}.")
    {
        StatusCode = statusCode;
        Body = body;
    }
}
