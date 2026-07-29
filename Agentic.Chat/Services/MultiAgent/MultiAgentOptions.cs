namespace Agentic.Chat.Services.MultiAgent;

public sealed class MultiAgentOptions
{
    /// <summary>Base URL for the self-hosted SearXNG metasearch backend. Leave empty to disable web search.</summary>
    public string SearXNGBaseUrl { get; set; } = "";

    /// <summary>Base URL for the self-hosted Ollama LLM endpoint. Leave empty to disable local LLM calls.</summary>
    public string OllamaBaseUrl { get; set; } = "";

    /// <summary>Whether the multi-agent council feature is enabled. When false, the UI hides the Council button.</summary>
    public bool CouncilEnabled { get; set; }

    /// <summary>Human-readable reason the Council is disabled. Empty when enabled.</summary>
    public string DisabledReason { get; set; } = "";
}
