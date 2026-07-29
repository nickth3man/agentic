using System.Net.Http.Headers;
using Agentic.Chat.Components;
using Agentic.Chat.Data;
using Agentic.Chat.Services;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddHttpClient<SearXNGSearchProvider>();
builder.Services.AddHttpClient<WikipediaSearchProvider>();
builder.Services.AddHttpClient<ArXivSearchProvider>();
builder.Services.AddHttpClient<OllamaLocalLlmClient>();
builder.Services.AddScoped<ISearchProvider>(sp => new CompositeSearchProvider([
    sp.GetRequiredService<SearXNGSearchProvider>(),
    sp.GetRequiredService<WikipediaSearchProvider>(),
    sp.GetRequiredService<ArXivSearchProvider>()
]));
builder.Services.AddScoped<Agentic.Chat.Services.MultiAgent.ILocalLlmClient, OllamaLocalLlmClient>();
builder.Services.AddScoped<Agentic.Chat.Services.MultiAgent.ResearchTeamCoordinator>();

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
