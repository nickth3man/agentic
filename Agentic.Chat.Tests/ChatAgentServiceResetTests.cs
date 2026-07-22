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

public class ChatAgentServiceResetTests
{
    [Fact]
    public async Task Reset_AfterCompletedSend_ClearsDisplayMessages()
    {
        var service = CreateService();
        await Consume(service.SendStreamingAsync("hello"));
        Assert.Equal(2, service.Messages.Count);

        service.Reset();

        Assert.Empty(service.Messages);
    }

    [Fact]
    public async Task Reset_AfterCompletedSend_NextRequestHasOnlySystemAndUser()
    {
        string? capturedBody = null;
        var service = CreateService(
            respond: _ => (SseBody("[DONE]"), HttpStatusCode.OK),
            captureRequest: r =>
            {
                capturedBody = r.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            });

        await Consume(service.SendStreamingAsync("first"));
        service.Reset();
        capturedBody = null;

        await Consume(service.SendStreamingAsync("second"));

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a helpful chat agent.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("second", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Reset_WhenEmpty_LeavesDisplayEmpty_AndNextSendIsSystemPlusUser()
    {
        string? capturedBody = null;
        var service = CreateService(
            respond: _ => (SseBody("[DONE]"), HttpStatusCode.OK),
            captureRequest: r =>
            {
                capturedBody = r.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            });

        Assert.Empty(service.Messages);
        service.Reset();
        Assert.Empty(service.Messages);

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("hi", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Reset_WhileStreaming_IsNoOp_ThenClearsAfterComplete()
    {
        var body = SseBody(
            "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}",
            "{\"choices\":[{\"delta\":{\"content\":\", world\"}}]}",
            "[DONE]");
        var service = CreateService(respond: _ => (body, HttpStatusCode.OK));

        await using var enumerator = service.SendStreamingAsync("hi").GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, service.Messages.Count);

        service.Reset();
        Assert.Equal(2, service.Messages.Count);

        while (await enumerator.MoveNextAsync())
        {
            /* drain */
        }

        Assert.Equal(2, service.Messages.Count);
        service.Reset();
        Assert.Empty(service.Messages);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static ChatAgentService CreateService(
        Func<HttpRequestMessage, (string Body, HttpStatusCode Status)>? respond = null,
        Action<HttpRequestMessage>? captureRequest = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<OpenRouterOptions>(o =>
        {
            o.BaseUrl = "https://test.local/";
            o.Model = "test-model";
        });
        services.AddHttpClient("OpenRouter", client =>
        {
            client.BaseAddress = new Uri("https://test.local/");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(
            respond ?? (_ => (SseBody("[DONE]"), HttpStatusCode.OK)),
            captureRequest));

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var options = provider.GetRequiredService<IOptions<OpenRouterOptions>>();
        var logger = provider.GetRequiredService<ILogger<ChatAgentService>>();

        var catalog = new ModelCatalogService(factory);
        catalog.SeedForTest(new[]
        {
            new OpenRouterModel(
                "test-model",
                "test-model",
                128_000L,
                DateTimeOffset.UtcNow,
                "text->text",
                new OpenRouterPricing(0.0000025m, 0.00001m),
                new[] { "tools", "reasoning", "tool_choice" })
        });

        var js = TestSupport.NewProtectedJSRuntime();
        var storage = new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest(null);

        return new ChatAgentService(factory, options, logger, selection, catalog);
    }

    private static async Task Consume(IAsyncEnumerable<ChatDisplayMessage> stream)
    {
        await foreach (var _ in stream)
        {
            /* drain */
        }
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
        private readonly Action<HttpRequestMessage>? _capture;

        public StubHandler(
            Func<HttpRequestMessage, (string Body, HttpStatusCode Status)> respond,
            Action<HttpRequestMessage>? capture)
        {
            _respond = respond;
            _capture = capture;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _capture?.Invoke(request);
            var (body, status) = _respond(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            });
        }
    }
}
