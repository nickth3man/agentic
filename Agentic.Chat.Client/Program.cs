using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Agentic.Chat;
using Agentic.Chat.Services;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Synchronously fetch wwwroot/app-config.json before registering MultiAgent
// services. Council availability requires a valid https:// SearXNG URL —
// Ollama is optional. Localhost / http endpoints are rejected on Pages to
// prevent the feature from silently returning no research data.
var initialHttp = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
var searxngUrl = "";
var ollamaUrl = "";
var councilDisabledReason = "Multi-agent council is not configured. Add multiAgent.searxngBaseUrl to wwwroot/app-config.json with a public HTTPS+CORS endpoint.";
try
{
    var cfg = await initialHttp.GetFromJsonAsync<JsonElement>("app-config.json");
    if (cfg.TryGetProperty("multiAgent", out var multi))
    {
        if (multi.TryGetProperty("searxngBaseUrl", out var s) && s.ValueKind == JsonValueKind.String)
            searxngUrl = s.GetString() ?? "";
        if (multi.TryGetProperty("ollamaBaseUrl", out var o) && o.ValueKind == JsonValueKind.String)
            ollamaUrl = o.GetString() ?? "";
    }
}
catch
{
    // app-config.json missing or malformed → council disabled
}
bool IsValidHttps(string u) => !string.IsNullOrWhiteSpace(u)
    && Uri.TryCreate(u, UriKind.Absolute, out var uri)
    && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
var searxngOk = IsValidHttps(searxngUrl);
var ollamaOk = IsValidHttps(ollamaUrl);
var councilEnabled = searxngOk; // require SearXNG
if (!councilEnabled)
{
    councilDisabledReason = searxngUrl == ""
        ? "Multi-agent council requires a SearXNG endpoint. Add multiAgent.searxngBaseUrl to wwwroot/app-config.json with a public HTTPS+CORS URL."
        : $"Multi-agent SearXNG URL is not a valid https:// endpoint (got: {searxngUrl}).";
}
else if (ollamaUrl != "" && !ollamaOk)
{
    councilDisabledReason = $"Ollama URL is not a valid https:// endpoint (got: {ollamaUrl}). Council will use the unavailable-LLM fallback.";
}
else if (!ollamaOk)
{
    councilDisabledReason = "Council enabled (SearXNG); local LLM synthesis disabled (Ollama not configured).";
}

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(5)
});
builder.Services.AddSingleton(Options.Create(new OpenRouterOptions
{
    BaseUrl = "https://openrouter.ai/api/v1",
    Model = "openrouter/free",
    HttpReferer = builder.HostEnvironment.BaseAddress,
    AppTitle = "Agentic Chat",
    SystemPrompt = OpenRouterOptions.DefaultSystemPrompt
}));
builder.Services.AddSingleton(Options.Create(new MultiAgentOptions
{
    SearXNGBaseUrl = searxngUrl,
    OllamaBaseUrl = ollamaUrl,
    CouncilEnabled = councilEnabled,
    DisabledReason = councilDisabledReason
}));
builder.Services.AddScoped<BrowserStorage>();
builder.Services.AddScoped<OpenRouterCredentialService>();
builder.Services.AddScoped<IOpenRouterClient, OpenRouterClient>();
builder.Services.AddScoped<ModelCatalogService>();
builder.Services.AddScoped<SelectedModelService>();
builder.Services.AddScoped<ModelPickerPreferencesService>();
builder.Services.AddScoped<SystemPromptService>();
builder.Services.AddScoped<ChatSettingsService>();
builder.Services.AddScoped<BrowserConversationStore>();
builder.Services.AddScoped<ConversationPersistence>();
builder.Services.AddScoped<IActiveConversationWriter>(
    services => services.GetRequiredService<ConversationPersistence>());
builder.Services.AddScoped<ChatAgentService>();
builder.Services.AddScoped<ConversationService>();

builder.Services.AddScoped<ISearchProvider>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MultiAgentOptions>>().Value;
    if (!IsValidHttps(opts.SearXNGBaseUrl))
    {
        return new CompositeSearchProvider([]);
    }
    var http = sp.GetRequiredService<HttpClient>();
    return new CompositeSearchProvider([
        new SearXNGSearchProvider(http, NullLogger<SearXNGSearchProvider>.Instance, opts.SearXNGBaseUrl),
        new WikipediaSearchProvider(http, NullLogger<WikipediaSearchProvider>.Instance),
        new ArXivSearchProvider(http, NullLogger<ArXivSearchProvider>.Instance)
    ]);
});
builder.Services.AddScoped<ILocalLlmClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MultiAgentOptions>>().Value;
    if (!IsValidHttps(opts.OllamaBaseUrl))
    {
        return new UnavailableLocalLlm();
    }
    var http = sp.GetRequiredService<HttpClient>();
    return new OllamaLocalLlmClient(http, NullLogger<OllamaLocalLlmClient>.Instance, opts.OllamaBaseUrl);
});
builder.Services.AddScoped<ResearchTeamCoordinator>();

await builder.Build().RunAsync();
