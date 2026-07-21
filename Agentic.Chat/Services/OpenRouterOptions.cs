namespace Agentic.Chat.Services;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    public string Model { get; set; } = "openai/gpt-oss-120b";

    public string HttpReferer { get; set; } = "http://localhost:5123";

    public string AppTitle { get; set; } = "Agentic Chat";
}
