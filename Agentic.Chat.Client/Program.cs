using Agentic.Chat;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

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

await builder.Build().RunAsync();
