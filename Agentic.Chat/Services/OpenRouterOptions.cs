namespace Agentic.Chat.Services;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    public string Model { get; set; } = "openai/gpt-oss-120b";

    public string HttpReferer { get; set; } = "http://localhost:5123";

    public string AppTitle { get; set; } = "Agentic Chat";

    // GATE-VERIFICATION (DO NOT MERGE): deliberately uncovered method used to confirm
    // the coverlet.msbuild Threshold=100 gate in Agentic.Chat.Tests.csproj fails CI on
    // any coverage regression. This PR exists only to verify the gate; it must be
    // closed without merging.
    public string GateVerificationUnusedHelper(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "empty";
        }
        return input.Trim();
    }
}
