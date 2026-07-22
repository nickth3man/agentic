using Agentic.Chat.Components;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

builder.Services.AddSingleton<ModelCatalogService>();
builder.Services.AddScoped<SelectedModelService>();
builder.Services.AddScoped<ProtectedLocalStorage>();
builder.Services.AddScoped<ChatAgentService>();

var app = builder.Build();

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
