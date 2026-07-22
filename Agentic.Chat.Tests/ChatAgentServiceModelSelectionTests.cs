using System.Net;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests;

// Tests for ChatAgentService's per-call model selection. The body-shaping change
// moved from a hardcoded `reasoning` key plus a fixed model id (read from
// OpenRouterOptions) to runtime composition:
//   - Model id: SelectedModelService.CurrentModelId ?? OpenRouterOptions.Model
//   - reasoning key: included only when the catalog's FindByIdAsync resolves the
//     id AND the resolved model has SupportsReasoning == true.
public class ChatAgentServiceModelSelectionTests
{
    private const string DefaultModel = "openai/gpt-oss-120b";

    [Fact]
    public async Task SendAsync_UsesSelectedModelId_WhenSet()
    {
        string? capturedBody = null;
        var service = BuildService(
            captured: body => capturedBody = body,
            selectedModelId: "anthropic/claude-3.5-sonnet",
            catalogModels: new[] { ("anthropic/claude-3.5-sonnet", true) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("anthropic/claude-3.5-sonnet", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SendAsync_FallsBackToOptionsModel_WhenSelectionNotLoaded()
    {
        string? capturedBody = null;
        var service = BuildService(
            captured: body => capturedBody = body,
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) });

        await Consume(service.SendStreamingAsync("hi"));

        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal(DefaultModel, doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SendAsync_IncludesReasoning_WhenModelSupportsIt()
    {
        string? capturedBody = null;
        var service = BuildService(
            captured: body => capturedBody = body,
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Contains("\"reasoning\"", capturedBody!);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.True(doc.RootElement.TryGetProperty("reasoning", out var reasoning));
        Assert.True(reasoning.GetProperty("enabled").GetBoolean());
        Assert.False(reasoning.GetProperty("exclude").GetBoolean());
    }

    [Fact]
    public async Task SendAsync_OmitsReasoning_WhenModelDoesNotSupportIt()
    {
        string? capturedBody = null;
        var service = BuildService(
            captured: body => capturedBody = body,
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, false) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.False(doc.RootElement.TryGetProperty("reasoning", out _),
            "reasoning key must be absent when the catalog says the model doesn't support it.");
    }

    [Fact]
    public async Task SendAsync_OmitsReasoning_WhenModelNotFoundInCatalog()
    {
        // Catalog returns null for the requested id (e.g. fallback default that isn't
        // present in the seeded list), so reasoning must NOT be included.
        string? capturedBody = null;
        var service = BuildService(
            captured: body => capturedBody = body,
            selectedModelId: null,
            catalogModels: Array.Empty<(string, bool)>());

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.False(doc.RootElement.TryGetProperty("reasoning", out _));
        Assert.Equal(DefaultModel, doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SendAsync_SelectedModelTakesPrecedenceOverCatalogFallback()
    {
        // Even if the catalog does not know about the selected id, we honor the
        // user's selection — the request still uses that id, just without reasoning.
        string? capturedBody = null;
        var service = BuildService(
            captured: body => capturedBody = body,
            selectedModelId: "newvendor/unknown-experimental",
            catalogModels: new[] { (DefaultModel, true) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("newvendor/unknown-experimental", doc.RootElement.GetProperty("model").GetString());
        Assert.False(doc.RootElement.TryGetProperty("reasoning", out _));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static ChatAgentService BuildService(
        Action<string>? captured,
        string? selectedModelId,
        (string Id, bool SupportsReasoning)[] catalogModels)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<OpenRouterOptions>(o =>
        {
            o.BaseUrl = "https://test.local/";
            o.Model = DefaultModel;
        });

        services.AddHttpClient("OpenRouter", client => client.BaseAddress = new Uri("https://test.local/"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(req =>
            {
                captured?.Invoke(req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()!);
                return (SseBody("[DONE]"), HttpStatusCode.OK);
            }));

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var opts = provider.GetRequiredService<IOptions<OpenRouterOptions>>();
        var logger = provider.GetRequiredService<ILogger<ChatAgentService>>();

        var catalog = new ModelCatalogService(factory);
        // Always seed (even with an empty list) so FindByIdAsync returns null on a
        // populated but empty cache, instead of triggering a real /models fetch
        // through the test's HTTP handler.
        catalog.SeedForTest(
            catalogModels.Select(m => new OpenRouterModel(
                m.Id,
                m.Id,
                128_000L,
                DateTimeOffset.UtcNow,
                "text->text",
                new OpenRouterPricing(0m, 0m),
                m.SupportsReasoning
                    ? new[] { "tools", "reasoning" }
                    : new[] { "tools" }))
                .ToList());

        var js = TestSupport.NewProtectedJSRuntime();
        var storage = new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest(selectedModelId);

        return new ChatAgentService(factory, opts, logger, selection, catalog);
    }

    private static async Task<List<ChatDisplayMessage>> Consume(IAsyncEnumerable<ChatDisplayMessage> stream)
    {
        var list = new List<ChatDisplayMessage>();
        await foreach (var m in stream) list.Add(m);
        return list;
    }

    private static string SseBody(params string[] payloads)
    {
        var sb = new StringBuilder();
        foreach (var p in payloads) sb.Append("data: ").Append(p).Append("\n\n");
        return sb.ToString();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (string Body, HttpStatusCode Status)> _respond;

        public StubHandler(Func<HttpRequestMessage, (string Body, HttpStatusCode Status)> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (body, status) = _respond(request);
            var msg = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(msg);
        }
    }
}
