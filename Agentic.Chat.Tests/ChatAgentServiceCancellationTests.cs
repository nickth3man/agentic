using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Agentic.Chat.Tests.Fixtures;

namespace Agentic.Chat.Tests;

// Issue #12 — cancellation mid-stream must leave ChatAgentService consistent:
// IsStreaming cleared, partial tokens kept, "(stopped)" display-only (never in
// the API transcript), and OperationCanceledException still propagated to Chat.razor.
public class ChatAgentServiceCancellationTests : IClassFixture<ChatAgentServiceFixture>
{
    private readonly ChatAgentServiceFixture _fixture;

    public ChatAgentServiceCancellationTests(ChatAgentServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MidStreamCancel_LeavesServiceStateConsistent()
    {
        var (service, _) = _fixture.CreateBuilder()
            .WithResponses(
                FakeOpenRouterClient.FakeResponse.Ok(
                    new StreamDelta("Hello", null),
                    new StreamDelta(" world", null),
                    new StreamDelta("!", null)))
            .Build();

        using var cts = new CancellationTokenSource();
        await using var enumerator = service.SendStreamingAsync("hi", cancellationToken: cts.Token).GetAsyncEnumerator();

        // Placeholder before any traffic.
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(enumerator.Current.IsStreaming);
        Assert.Equal(string.Empty, enumerator.Current.Content);

        // First content delta.
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("Hello", enumerator.Current.Content);
        Assert.True(enumerator.Current.IsStreaming);

        cts.Cancel();

        // FinalizeCancelledAssistant yields the stopped assistant, then rethrows on the
        // next MoveNextAsync so Chat.razor can set statusText without an error banner.
        Assert.True(await enumerator.MoveNextAsync());
        var assistant = enumerator.Current;
        Assert.False(assistant.IsStreaming);
        Assert.False(assistant.IsError);
        Assert.Equal("Hello (stopped)", assistant.Content);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await enumerator.MoveNextAsync());

        // Display transcript: user + stopped assistant.
        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("user", service.Messages[0].Role);
        Assert.Equal("hi", service.Messages[0].Content);
        Assert.Equal("assistant", service.Messages[1].Role);
        Assert.False(service.Messages[1].IsStreaming);
        Assert.Contains("(stopped)", service.Messages[1].Content);
        Assert.StartsWith("Hello", service.Messages[1].Content);

        // API transcript: system + user + partial assistant — no display marker.
        Assert.Equal(3, service.ApiMessagesForTest.Count);
        Assert.Equal("system", service.ApiMessagesForTest[0].Role);
        Assert.Equal("user", service.ApiMessagesForTest[1].Role);
        Assert.Equal("assistant", service.ApiMessagesForTest[2].Role);
        Assert.Equal("Hello", service.ApiMessagesForTest[2].TextContent);
        Assert.DoesNotContain("(stopped)", service.ApiMessagesForTest[2].TextContent);
    }

    [Fact]
    public async Task PreCanceled_NoPartialContent_StoppedMarkerDisplayOnly()
    {
        var (service, _) = _fixture.CreateBuilder().Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi", cancellationToken: cts.Token)));

        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("assistant", service.Messages[1].Role);
        Assert.False(service.Messages[1].IsStreaming);
        Assert.Equal("(stopped)", service.Messages[1].Content);

        // No partial tokens → no assistant entry in the API transcript.
        Assert.Equal(2, service.ApiMessagesForTest.Count);
        Assert.Equal("system", service.ApiMessagesForTest[0].Role);
        Assert.Equal("user", service.ApiMessagesForTest[1].Role);
        Assert.All(service.ApiMessagesForTest, m => Assert.DoesNotContain("(stopped)", m.TextContent));
    }

    [Fact]
    public async Task MidStreamCancel_ReasoningOnly_CommitsReasoningWithoutStoppedMarker()
    {
        var (service, _) = _fixture.CreateBuilder()
            .WithResponses(
                FakeOpenRouterClient.FakeResponse.Ok(
                    new StreamDelta(null, "think"),
                    new StreamDelta(null, "ing"),
                    new StreamDelta("answer", null)))
            .Build();

        using var cts = new CancellationTokenSource();
        await using var enumerator = service.SendStreamingAsync("hi", cancellationToken: cts.Token).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync()); // placeholder
        Assert.True(await enumerator.MoveNextAsync()); // reasoning "think"
        Assert.Equal("think", enumerator.Current.Reasoning);
        Assert.Equal(string.Empty, enumerator.Current.Content);

        cts.Cancel();

        Assert.True(await enumerator.MoveNextAsync()); // stopped finalize
        Assert.False(enumerator.Current.IsStreaming);
        Assert.Equal("(stopped)", enumerator.Current.Content);
        Assert.Equal("think", enumerator.Current.Reasoning);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await enumerator.MoveNextAsync());

        Assert.Equal(3, service.ApiMessagesForTest.Count);
        var apiAssistant = service.ApiMessagesForTest[2];
        Assert.Equal("assistant", apiAssistant.Role);
        Assert.Equal(string.Empty, apiAssistant.TextContent);
        Assert.Equal("think", apiAssistant.Reasoning);
        Assert.DoesNotContain("(stopped)", apiAssistant.TextContent);
        Assert.DoesNotContain("(stopped)", apiAssistant.Reasoning ?? string.Empty);
    }

    [Fact]
    public async Task PreCanceled_BeforeCatalogLookup_FinalizesStopped()
    {
        // Covers ThrowIfCancellationRequested before FindByIdAsync — a pre-cancelled
        // token must clear IsStreaming even when the catalog cache hit would otherwise
        // skip its own CT checks.
        var (service, _) = _fixture.CreateBuilder().Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi", cancellationToken: cts.Token)));

        Assert.Equal(2, service.Messages.Count);
        Assert.False(service.Messages[1].IsStreaming);
        Assert.Equal("(stopped)", service.Messages[1].Content);
        Assert.Equal(2, service.ApiMessagesForTest.Count);
    }
}
