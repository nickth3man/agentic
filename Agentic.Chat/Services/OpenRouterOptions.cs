namespace Agentic.Chat.Services;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public const string DefaultSystemPrompt = "You are a helpful chat agent.";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    public string Model { get; set; } = "openai/gpt-oss-120b";

    public string HttpReferer { get; set; } = "http://localhost:5123";

    public string AppTitle { get; set; } = "Agentic Chat";

    /// <summary>
    /// Configured system prompt used when the user has not overridden it in the UI.
    /// Bound from appsettings. Consumers fall back to <see cref="DefaultSystemPrompt"/>
    /// when this value is missing or whitespace; an empty bound value stays empty here.
    /// </summary>
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;
}
