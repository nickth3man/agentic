using System.Net;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Chat.Tests;

public class OpenRouterClientTests
{
    [Fact]
    public async Task StreamChatAsync_AccumulatesContentDeltas()
    {
        var handler = new StubHandler(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\", world\"}}]}\n\n" +
            "data: [DONE]\n\n");
        using var provider = BuildProvider(handler);
        var client = new OpenRouterClient(provider.GetRequiredService<IHttpClientFactory>());

        var deltas = await Collect(client.StreamChatAsync(TestRequest()));

        Assert.Equal(2, deltas.Count);
        Assert.Equal(new StreamDelta("Hello", null), deltas[0]);
        Assert.Equal(new StreamDelta(", world", null), deltas[1]);
    }

    [Fact]
    public async Task StreamChatAsync_FiltersNonDataLines()
    {
        var handler = new StubHandler(
            "event: ping\n" +
            ":comment\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\n");
        using var provider = BuildProvider(handler);
        var client = new OpenRouterClient(provider.GetRequiredService<IHttpClientFactory>());

        var deltas = await Collect(client.StreamChatAsync(TestRequest()));

        var delta = Assert.Single(deltas);
        Assert.Equal("ok", delta.Content);
        Assert.Null(delta.Reasoning);
    }

    [Fact]
    public async Task StreamChatAsync_StopsAtDoneMarker()
    {
        var handler = new StubHandler(
            "data: {\"choices\":[{\"delta\":{\"content\":\"first\"}}]}\n\n" +
            "data: [DONE]\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"second\"}}]}\n\n");
        using var provider = BuildProvider(handler);
        var client = new OpenRouterClient(provider.GetRequiredService<IHttpClientFactory>());

        var deltas = await Collect(client.StreamChatAsync(TestRequest()));

        var delta = Assert.Single(deltas);
        Assert.Equal("first", delta.Content);
    }

    [Fact]
    public async Task StreamChatAsync_NonSuccess_ThrowsOpenRouterException()
    {
        var handler = new StubHandler("rate limited", HttpStatusCode.BadRequest);
        using var provider = BuildProvider(handler);
        var client = new OpenRouterClient(provider.GetRequiredService<IHttpClientFactory>());

        var exception = await Assert.ThrowsAsync<OpenRouterException>(
            () => Collect(client.StreamChatAsync(TestRequest())));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("rate limited", exception.Body);
    }

    [Fact]
    public async Task StreamChatAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var handler = new StubHandler("data: [DONE]\n\n");
        using var provider = BuildProvider(handler);
        var client = new OpenRouterClient(provider.GetRequiredService<IHttpClientFactory>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Collect(client.StreamChatAsync(TestRequest(), cts.Token)));
    }

    [Fact]
    public async Task StreamChatAsync_RequestShape()
    {
        var handler = new StubHandler("data: [DONE]\n\n");
        using var provider = BuildProvider(handler);
        var client = new OpenRouterClient(provider.GetRequiredService<IHttpClientFactory>());
        var request = new ChatCompletionRequest(
            "test-model",
            new[] { new ApiChatMessage("system", "sys", null) },
            Stream: true,
            Reasoning: new ReasoningRequest(Enabled: true, Exclude: false));

        _ = await Collect(client.StreamChatAsync(request));

        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal(new Uri("https://test.local/chat/completions"), handler.CapturedUri);
        Assert.NotNull(handler.CapturedBody);
        Assert.Contains("\"stream\":true", handler.CapturedBody!);
        Assert.Contains("\"model\":\"test-model\"", handler.CapturedBody);
        Assert.Contains("\"reasoning\"", handler.CapturedBody);
    }

    [Fact]
    public void SerializedRequestBody_IsByteIdenticalToLegacy()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var nonReasoningMessages = new[]
        {
            new ApiChatMessage("system", "sys", null),
            new ApiChatMessage("user", "hi", null),
            new ApiChatMessage("assistant", "ans", null)
        };
        var nonReasoning = JsonSerializer.Serialize(
            new ChatCompletionRequest("m", nonReasoningMessages, true, null),
            options);
        Assert.Equal(
            "{\"model\":\"m\",\"messages\":[{\"role\":\"system\",\"content\":\"sys\"},{\"role\":\"user\",\"content\":\"hi\"},{\"role\":\"assistant\",\"content\":\"ans\"}],\"stream\":true}",
            nonReasoning);

        var reasoning = JsonSerializer.Serialize(
            new ChatCompletionRequest(
                "m",
                new[] { new ApiChatMessage("system", "sys", null) },
                true,
                new ReasoningRequest(true, false)),
            options);
        Assert.Equal(
            "{\"model\":\"m\",\"messages\":[{\"role\":\"system\",\"content\":\"sys\"}],\"stream\":true,\"reasoning\":{\"enabled\":true,\"exclude\":false}}",
            reasoning);

        var assistantReasoning = JsonSerializer.Serialize(
            new ChatCompletionRequest(
                "m",
                new[] { new ApiChatMessage("assistant", "a", "r") },
                true,
                null),
            options);
        Assert.Equal(
            "{\"model\":\"m\",\"messages\":[{\"role\":\"assistant\",\"content\":\"a\",\"reasoning\":\"r\"}],\"stream\":true}",
            assistantReasoning);
    }

    private static ServiceProvider BuildProvider(StubHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("OpenRouter", client =>
            {
                client.BaseAddress = new Uri("https://test.local/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static ChatCompletionRequest TestRequest()
        => new(
            "test-model",
            new[] { new ApiChatMessage("user", "hi", null) },
            Stream: true,
            Reasoning: null);

    private static async Task<List<StreamDelta>> Collect(IAsyncEnumerable<StreamDelta> stream)
    {
        var deltas = new List<StreamDelta>();
        await foreach (var delta in stream)
        {
            deltas.Add(delta);
        }
        return deltas;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public HttpMethod? CapturedMethod { get; private set; }
        public Uri? CapturedUri { get; private set; }
        public string? CapturedBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedMethod = request.Method;
            CapturedUri = request.RequestUri;
            CapturedBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/event-stream")
            });
        }
    }
}
