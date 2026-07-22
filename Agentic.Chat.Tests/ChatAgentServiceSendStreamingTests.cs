using System.Net;
using System.Text;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests;

public class ChatAgentServiceSendStreamingTests
{
    [Fact]
    public async Task NullUserText_Throws()
    {
        var service = CreateService();

        // ArgumentException.ThrowIfNullOrWhiteSpace(null!) throws ArgumentNullException
        // (which derives from ArgumentException), so use ThrowsAnyAsync.
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Consume(service.SendStreamingAsync(null!)));
    }

    [Fact]
    public async Task EmptyUserText_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Consume(service.SendStreamingAsync(string.Empty)));
    }

    [Fact]
    public async Task WhitespaceUserText_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Consume(service.SendStreamingAsync("   ")));
    }

    [Fact]
    public async Task AddsUserAndAssistantMessages_ToMessagesList()
    {
        var service = CreateService();

        await Consume(service.SendStreamingAsync("hello"));

        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("user", service.Messages[0].Role);
        Assert.Equal("hello", service.Messages[0].Content);
        Assert.Equal("assistant", service.Messages[1].Role);
        // Empty stream -> "(No response content returned.)" placeholder.
        Assert.Equal("(No response content returned.)", service.Messages[1].Content);
    }

    [Fact]
    public async Task TrimsUserText()
    {
        var service = CreateService();

        await Consume(service.SendStreamingAsync("  hi there  "));

        Assert.Equal("hi there", service.Messages[0].Content);
    }

    [Fact]
    public async Task HappyPath_AccumulatesContentDeltas()
    {
        var body = SseBody(
            "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}",
            "{\"choices\":[{\"delta\":{\"content\":\", world\"}}]}",
            "[DONE]");
        var service = CreateService(respond: _ => (body, HttpStatusCode.OK));

        await Consume(service.SendStreamingAsync("hi"));

        var assistant = service.Messages[1];
        Assert.Equal("Hello, world", assistant.Content);
        Assert.False(assistant.IsStreaming);
    }

    [Fact]
    public async Task YieldsAssistantReference_AfterEachDelta()
    {
        var body = SseBody(
            "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}",
            "{\"choices\":[{\"delta\":{\"content\":\", world\"}}]}",
            "[DONE]");
        var service = CreateService(respond: _ => (body, HttpStatusCode.OK));

        // Snapshot state at each yield: the shared instance is mutated in place, so reading
        // it after the loop completes would only show the final state.
        var snapshots = new List<(ChatDisplayMessage Ref, bool IsStreaming, string Content)>();
        await foreach (var m in service.SendStreamingAsync("hi"))
        {
            snapshots.Add((m, m.IsStreaming, m.Content));
        }

        // 1 placeholder + 2 delta yields + 1 final yield = 4
        Assert.Equal(4, snapshots.Count);
        var sharedRef = snapshots[0].Ref;
        Assert.All(snapshots, s => Assert.Same(sharedRef, s.Ref));
        // Placeholder arrives before any HTTP traffic.
        Assert.True(snapshots[0].IsStreaming);
        Assert.Equal(string.Empty, snapshots[0].Content);
        // Each delta yield still has IsStreaming=true (set to false only after the loop).
        Assert.True(snapshots[1].IsStreaming);
        Assert.Equal("Hello", snapshots[1].Content);
        Assert.True(snapshots[2].IsStreaming);
        Assert.Equal("Hello, world", snapshots[2].Content);
        // Final yield after IsStreaming was set to false.
        Assert.False(snapshots[3].IsStreaming);
        Assert.Equal("Hello, world", snapshots[3].Content);
    }

    [Fact]
    public async Task FiltersNonDataLines()
    {
        var body =
            "event: ping\n" +
            ":comment\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\n";
        var service = CreateService(respond: _ => (body, HttpStatusCode.OK));

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("ok", service.Messages[1].Content);
    }

    [Fact]
    public async Task StopsAtDoneMarker()
    {
        var body = SseBody(
            "{\"choices\":[{\"delta\":{\"content\":\"first\"}}]}",
            "[DONE]",
            "{\"choices\":[{\"delta\":{\"content\":\"second\"}}]}");
        var service = CreateService(respond: _ => (body, HttpStatusCode.OK));

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("first", service.Messages[1].Content);
    }

    [Fact]
    public async Task NonSuccessStatus_SetsErrorMessage()
    {
        var service = CreateService(respond: _ => ("rate limited", HttpStatusCode.BadRequest));

        var messages = await Consume(service.SendStreamingAsync("hi"));
        var assistant = messages[^1];

        Assert.StartsWith("(Error 400:", assistant.Content);
        Assert.Contains("rate limited", assistant.Content);
        Assert.False(assistant.IsStreaming);
    }

    [Fact]
    public async Task ErrorBody_TruncatedAt300Chars()
    {
        var body = new string('x', 400);
        var service = CreateService(respond: _ => (body, HttpStatusCode.BadRequest));

        var messages = await Consume(service.SendStreamingAsync("hi"));
        var assistant = messages[^1];

        const string prefix = "(Error 400: ";
        const string suffix = ")";
        Assert.StartsWith(prefix, assistant.Content);
        Assert.EndsWith(suffix, assistant.Content);

        // Slice between the known prefix and trailing ")"; should be body[..300] + ellipsis.
        var inner = assistant.Content[prefix.Length..^suffix.Length];
        Assert.Equal(body[..300] + "\u2026", inner);
    }

    [Fact]
    public async Task EmptyStream_SetsNoResponseContent()
    {
        var body = SseBody("[DONE]");
        var service = CreateService(respond: _ => (body, HttpStatusCode.OK));

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("(No response content returned.)", service.Messages[1].Content);
    }

    [Fact]
    public async Task ReasoningStream_Accumulated()
    {
        var body = SseBody(
            "{\"choices\":[{\"delta\":{\"reasoning\":\"think\"}}]}",
            "{\"choices\":[{\"delta\":{\"reasoning\":\"ing\"}}]}",
            "{\"choices\":[{\"delta\":{\"content\":\"answer\"}}]}",
            "[DONE]");
        var service = CreateService(respond: _ => (body, HttpStatusCode.OK));

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("thinking", service.Messages[1].Reasoning);
        Assert.Equal("answer", service.Messages[1].Content);
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Consume(service.SendStreamingAsync("hi", cts.Token)));
    }

    [Fact]
    public async Task CapturesExpectedRequestShape()
    {
        // HttpClient disposes the request after SendAsync, so we must read the body
        // synchronously inside the capture. StringContent is buffered, so the sync read
        // is safe.
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var service = CreateService(
            respond: _ => (SseBody("[DONE]"), HttpStatusCode.OK),
            captureRequest: r =>
            {
                captured = r;
                capturedBody = r.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(new Uri("https://test.local/chat/completions"), captured.RequestUri);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"stream\":true", capturedBody!);
        Assert.Contains("\"model\":\"test-model\"", capturedBody);
        Assert.Contains("\"reasoning\"", capturedBody);
    }

    // ---------- helpers ----------

    private static ChatAgentService CreateService(
        Func<HttpRequestMessage, (string Body, HttpStatusCode Status)>? respond = null,
        Action<HttpRequestMessage>? captureRequest = null,
        string? selectedModelId = null,
        string? catalogId = "test-model",
        bool catalogSupportsReasoning = true)
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
            // Production (Program.cs) sets BaseAddress so the relative "chat/completions"
            // URI resolves. Mirror that here.
            client.BaseAddress = new Uri("https://test.local/");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(
            respond ?? (_ => (string.Empty, HttpStatusCode.OK)),
            captureRequest));

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var options = provider.GetRequiredService<IOptions<OpenRouterOptions>>();
        var logger = provider.GetRequiredService<ILogger<ChatAgentService>>();

        var catalog = new ModelCatalogService(factory);
        if (catalogId is not null)
        {
            catalog.SeedForTest(new[]
            {
                new Agentic.Chat.Models.OpenRouterModel(
                    catalogId,
                    catalogId,
                    128_000L,
                    DateTimeOffset.UtcNow,
                    "text->text",
                    new Agentic.Chat.Models.OpenRouterPricing(0.0000025m, 0.00001m),
                    catalogSupportsReasoning
                        ? new[] { "tools", "reasoning", "tool_choice" }
                        : new[] { "tools", "tool_choice" })
            });
        }

        // SelectedModelService with a working protected local storage. The model ID
        // is set via the test seam — making the storage round-trip unnecessary for
        // tests that just need a deterministic CurrentModelId.
        var js = TestSupport.NewProtectedJSRuntime();
        var storage = new Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage(
            js, new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        // SetCurrentModelIdForTest sets IsLoaded=true and raises OnChange, matching
        // the post-LoadAsync state for both the "stored" and "not stored" branches.
        selection.SetCurrentModelIdForTest(selectedModelId);

        return new ChatAgentService(factory, options, logger, selection, catalog);
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
            var msg = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(msg);
        }
    }
}
