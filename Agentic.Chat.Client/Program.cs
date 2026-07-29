using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Agentic.Chat;
using Agentic.Chat.Client.Services;
using Agentic.Chat.Services;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Synchronously fetch wwwroot/app-config.json before registering MultiAgent services.
// The Pages deployment does not ship a static API key; the council stays disabled
// until the visitor enters their own OpenRouter key. SearXNG/Ollama URLs are kept
// as overrides for users self-hosting a full backend.
var initialHttp = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
var searxngUrl = "";
var ollamaUrl = "";
var councilDisabledReason = "Multi-agent council needs your OpenRouter API key. Open the key settings (top-right of the chat header) to add one.";
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
// SearXNG/Ollama are now optional overrides. The default reason is the
// "no OpenRouter key" prompt; if the deployer misconfigured SearXNG/Ollama we
// surface that and leave the council disabled.
if (!searxngOk && !ollamaOk && (searxngUrl != "" || ollamaUrl != ""))
    councilDisabledReason = "Multi-agent council requires both SearXNG and Ollama endpoints in wwwroot/app-config.json (multiAgent.searxngBaseUrl and multiAgent.ollamaBaseUrl), each a public HTTPS+CORS URL.";
else if (!searxngOk && searxngUrl != "")
    councilDisabledReason = searxngUrl == ""
        ? "Multi-agent SearXNG URL is required."
        : $"Multi-agent SearXNG URL is not a valid https:// endpoint (got: {searxngUrl}).";
else if (!ollamaOk && ollamaUrl != "")
    councilDisabledReason = ollamaUrl == ""
        ? "Multi-agent Ollama URL is required."
        : $"Multi-agent Ollama URL is not a valid https:// endpoint (got: {ollamaUrl}).";

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
    CouncilEnabled = searxngOk && ollamaOk,
    DisabledReason = councilDisabledReason
}));
builder.Services.AddScoped<ICouncilCapabilities, CouncilCapabilities>();

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
    var http = sp.GetRequiredService<HttpClient>();
    // Always-available providers (no key, CORS-* or origin=*): Wikipedia + ArXiv + Mwmbl.
    // CompositeSearchProvider guards each child so a CORS rejection on one (e.g. ArXiv on
    // Pages) cannot poison the others. SearXNG is added on top when a base URL is configured.
    var providers = new List<ISearchProvider>
    {
        new WikipediaSearchProvider(http, NullLogger<WikipediaSearchProvider>.Instance),
        new ArXivSearchProvider(http, NullLogger<ArXivSearchProvider>.Instance),
        new MwmblSearchProvider(http, NullLogger<MwmblSearchProvider>.Instance),
    };
    if (IsValidHttps(opts.SearXNGBaseUrl))
    {
        providers.Add(new SearXNGSearchProvider(http, NullLogger<SearXNGSearchProvider>.Instance, opts.SearXNGBaseUrl));
    }
    return new CompositeSearchProvider(providers, NullLogger<CompositeSearchProvider>.Instance);
});
builder.Services.AddScoped<ILocalLlmClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MultiAgentOptions>>().Value;
    var http = sp.GetRequiredService<HttpClient>();
    return opts.SearXNGBaseUrl != "" && opts.OllamaBaseUrl != "" && IsValidHttps(opts.OllamaBaseUrl)
        ? new OllamaLocalLlmClient(http, NullLogger<OllamaLocalLlmClient>.Instance, opts.OllamaBaseUrl)
        : new OpenRouterLocalLlmClient(
            http,
            sp.GetRequiredService<OpenRouterCredentialService>(),
            sp.GetRequiredService<IOptions<OpenRouterOptions>>(),
            NullLogger<OpenRouterLocalLlmClient>.Instance);
});
builder.Services.AddScoped<ResearchTeamCoordinator>();

await builder.Build().RunAsync();
