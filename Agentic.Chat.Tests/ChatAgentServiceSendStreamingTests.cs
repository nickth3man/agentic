using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
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
        var service = CreateService(new[]
        {
            new StreamDelta("Hello", null),
            new StreamDelta(", world", null)
        });

        await Consume(service.SendStreamingAsync("hi"));

        var assistant = service.Messages[1];
        Assert.Equal("Hello, world", assistant.Content);
        Assert.False(assistant.IsStreaming);
    }

    [Fact]
    public async Task YieldsAssistantReference_AfterEachDelta()
    {
        var service = CreateService(new[]
        {
            new StreamDelta("Hello", null),
            new StreamDelta(", world", null)
        });

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
    public async Task NonSuccess_SetsErrorMessage()
    {
        var service = CreateService(exception: new OpenRouterException(400, "rate limited"));

        var messages = await Consume(service.SendStreamingAsync("hi"));
        var assistant = messages[^1];

        Assert.StartsWith("(Error 400:", assistant.Content);
        Assert.Contains("rate limited", assistant.Content);
        Assert.False(assistant.IsStreaming);
        Assert.True(assistant.IsError);
    }

    [Fact]
    public async Task ErrorBody_TruncatedAt300Chars()
    {
        var body = new string('x', 400);
        var service = CreateService(exception: new OpenRouterException(400, body));

        var messages = await Consume(service.SendStreamingAsync("hi"));
        var assistant = messages[^1];

        const string prefix = "(Error 400: ";
        const string suffix = ")";
        Assert.StartsWith(prefix, assistant.Content);
        Assert.EndsWith(suffix, assistant.Content);

        // Slice between the known prefix and trailing ")"; should be body[..300] + ellipsis.
        var inner = assistant.Content[prefix.Length..^suffix.Length];
        Assert.Equal(body[..300] + "\u2026", inner);
        Assert.True(assistant.IsError);
    }

    [Fact]
    public async Task EmptyStream_SetsNoResponseContent()
    {
        var service = CreateService();

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("(No response content returned.)", service.Messages[1].Content);
    }

    [Fact]
    public async Task ReasoningStream_Accumulated()
    {
        var service = CreateService(new[]
        {
            new StreamDelta(null, "think"),
            new StreamDelta(null, "ing"),
            new StreamDelta("answer", null)
        });

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

    // ---------- helpers ----------

    private static ChatAgentService CreateService(
        IEnumerable<StreamDelta>? deltas = null,
        Exception? exception = null,
        string? selectedModelId = null,
        string? catalogId = "test-model",
        bool catalogSupportsReasoning = true)
    {
        var fakeClient = new FakeOpenRouterClient(deltas, exception);
        var options = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = "test-model"
        });
        var logger = NullLogger<ChatAgentService>.Instance;
        var catalog = new ModelCatalogService(new UnusedHttpClientFactory());
        if (catalogId is not null)
        {
            catalog.SeedForTest(new[]
            {
                new OpenRouterModel(
                    catalogId,
                    catalogId,
                    128_000L,
                    DateTimeOffset.UtcNow,
                    "text->text",
                    new OpenRouterPricing(0.0000025m, 0.00001m),
                    catalogSupportsReasoning
                        ? new[] { "tools", "reasoning", "tool_choice" }
                        : new[] { "tools", "tool_choice" })
            });
        }

        // SetCurrentModelIdForTest sets IsLoaded=true and raises OnChange, matching
        // the post-LoadAsync state for both the "stored" and "not stored" branches.
        var js = TestSupport.NewProtectedJSRuntime();
        var storage = new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest(selectedModelId);

        return new ChatAgentService(fakeClient, options, logger, selection, catalog);
    }

    private static async Task<List<ChatDisplayMessage>> Consume(IAsyncEnumerable<ChatDisplayMessage> stream)
    {
        var list = new List<ChatDisplayMessage>();
        await foreach (var m in stream) list.Add(m);
        return list;
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("The seeded model catalog must not fetch models in this test.");
    }
}
