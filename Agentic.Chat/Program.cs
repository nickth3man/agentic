using System;
using System.Net.Http.Headers;
using Agentic.Chat.Components;
using Agentic.Chat.Data;
using Agentic.Chat.Services;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Vision attachments can send multi-MB base64 over the circuit.
builder.Services.Configure<HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
});

var openRouterSection = builder.Configuration.GetSection(OpenRouterOptions.SectionName);
builder.Services.Configure<OpenRouterOptions>(openRouterSection);

var openRouterOptions = openRouterSection.Get<OpenRouterOptions>()
    ?? throw new InvalidOperationException("Missing configuration section: OpenRouter");

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "OPENROUTER_API_KEY environment variable is not set. " +
        "Set it before running the app (do not put secrets in appsettings).");
}

builder.Services.AddHttpClient("OpenRouter", client =>
{
    client.BaseAddress = new Uri(openRouterOptions.BaseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    client.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", openRouterOptions.HttpReferer);
    client.DefaultRequestHeaders.TryAddWithoutValidation("X-OpenRouter-Title", openRouterOptions.AppTitle);
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Conversation store (issue #13): local SQLite file under App_Data — path only,
// never a credentialed connection string.
var connectionString = ChatDatabase.GetConnectionString(builder.Environment.ContentRootPath);

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<IOpenRouterClient, OpenRouterClient>();
builder.Services.AddSingleton<ModelCatalogService>();
builder.Services.AddScoped<SelectedModelService>();
builder.Services.AddScoped<ModelPickerPreferencesService>();
builder.Services.AddScoped<SystemPromptService>();
builder.Services.AddScoped<ChatSettingsService>();
builder.Services.AddScoped<ProtectedLocalStorage>();
builder.Services.AddScoped<ConversationPersistence>();
builder.Services.AddScoped<IActiveConversationWriter>(sp =>
    sp.GetRequiredService<ConversationPersistence>());
builder.Services.AddScoped<ChatAgentService>();
builder.Services.AddScoped<ConversationService>();

// Multi-agent endpoints: read from configuration so Blazor Server deployments
// can target a public HTTPS SearXNG/Ollama backend (required for any non-local
// deployment, including GitHub Pages demo of the WASM client).
var multiAgentSection = builder.Configuration.GetSection("MultiAgent");
builder.Services.Configure<MultiAgentOptions>(multiAgentSection);
var searxngBase = multiAgentSection["SearXNGBaseUrl"];
var ollamaBase = multiAgentSection["OllamaBaseUrl"];
var searxngOk = !string.IsNullOrWhiteSpace(searxngBase) && Uri.TryCreate(searxngBase, UriKind.Absolute, out var sUri)
    && string.Equals(sUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
var ollamaOk = !string.IsNullOrWhiteSpace(ollamaBase) && Uri.TryCreate(ollamaBase, UriKind.Absolute, out var oUri)
    && string.Equals(oUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
var bothOk = searxngOk && ollamaOk;
var serverDisabled = !searxngOk && ollamaBase == ""
    ? "Council requires public HTTPS SearXNG and Ollama endpoints (MultiAgent:SearXNGBaseUrl and MultiAgent:OllamaBaseUrl)."
    : !searxngOk
        ? "Council requires a public HTTPS SearXNG endpoint (MultiAgent:SearXNGBaseUrl)."
        : !ollamaOk
            ? "Council requires a public HTTPS Ollama endpoint (MultiAgent:OllamaBaseUrl)."
            : "";
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new MultiAgentOptions
{
    SearXNGBaseUrl = searxngBase ?? "",
    OllamaBaseUrl = ollamaBase ?? "",
    CouncilEnabled = bothOk,
    DisabledReason = serverDisabled
}));

if (searxngOk)
{
    builder.Services.AddHttpClient<SearXNGSearchProvider>(c => c.BaseAddress = new Uri(searxngBase!));
}
else
{
    builder.Services.AddScoped<SearXNGSearchProvider>(_ =>
        new SearXNGSearchProvider(new HttpClient(), Microsoft.Extensions.Logging.Abstractions.NullLogger<SearXNGSearchProvider>.Instance, "http://invalid"));
}
builder.Services.AddHttpClient<WikipediaSearchProvider>();
builder.Services.AddHttpClient<ArXivSearchProvider>();

builder.Services.AddScoped<ISearchProvider>(sp => new CompositeSearchProvider(
    searxngOk
        ? new ISearchProvider[]
        {
            sp.GetRequiredService<SearXNGSearchProvider>(),
            sp.GetRequiredService<WikipediaSearchProvider>(),
            sp.GetRequiredService<ArXivSearchProvider>()
        }
        : Array.Empty<ISearchProvider>()));

if (ollamaOk)
{
    builder.Services.AddHttpClient<OllamaLocalLlmClient>(c => c.BaseAddress = new Uri(ollamaBase!));
    builder.Services.AddScoped<ILocalLlmClient, OllamaLocalLlmClient>();
}
else
{
    builder.Services.AddScoped<ILocalLlmClient, UnavailableLocalLlm>();
}

builder.Services.AddSingleton<ICouncilCapabilities, StaticCouncilCapabilities>();

builder.Services.AddScoped<ResearchTeamCoordinator>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    await ChatDatabase.EnsureCreatedAndMigratedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
