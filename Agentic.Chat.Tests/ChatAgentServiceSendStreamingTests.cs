using System.Text.Json;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Agentic.Chat.Tests.Fixtures;

namespace Agentic.Chat.Tests;

public class ChatAgentServiceSendStreamingTests : IClassFixture<ChatAgentServiceFixture>
{
    private readonly ChatAgentServiceFixture _fixture;

    public ChatAgentServiceSendStreamingTests(ChatAgentServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task NullUserText_Throws()
    {
        var (service, _) = _fixture.CreateBuilder().Build();

        // ArgumentException.ThrowIfNullOrWhiteSpace(null!) throws ArgumentNullException
        // (which derives from ArgumentException), so use ThrowsAnyAsync.
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync(null!)));
    }

    [Fact]
    public async Task EmptyUserText_Throws()
    {
        var (service, _) = _fixture.CreateBuilder().Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync(string.Empty)));
    }

    [Fact]
    public async Task WhitespaceUserText_Throws()
    {
        var (service, _) = _fixture.CreateBuilder().Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("   ")));
    }

    [Fact]
    public async Task AddsUserAndAssistantMessages_ToMessagesList()
    {
        var (service, _) = _fixture.CreateBuilder().Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hello"));

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
        var (service, _) = _fixture.CreateBuilder().Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("  hi there  "));

        Assert.Equal("hi there", service.Messages[0].Content);
    }

    [Fact]
    public async Task HappyPath_AccumulatesContentDeltas()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas(new[] {
            new StreamDelta("Hello", null),
            new StreamDelta(", world", null)
        }).Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        var assistant = service.Messages[1];
        Assert.Equal("Hello, world", assistant.Content);
        Assert.False(assistant.IsStreaming);
    }

    [Fact]
    public async Task YieldsAssistantReference_AfterEachDelta()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas(new[] {
            new StreamDelta("Hello", null),
            new StreamDelta(", world", null)
        }).Build();

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
        var (service, _) = _fixture.CreateBuilder()
            .WithException(new OpenRouterException(400, "rate limited"))
            .Build();

        var messages = await ChatAgentServiceTestHelpers.ConsumeToList(service.SendStreamingAsync("hi"));
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
        var (service, _) = _fixture.CreateBuilder()
            .WithException(new OpenRouterException(400, body))
            .Build();

        var messages = await ChatAgentServiceTestHelpers.ConsumeToList(service.SendStreamingAsync("hi"));
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
        var (service, _) = _fixture.CreateBuilder().Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("(No response content returned.)", service.Messages[1].Content);
    }

    [Fact]
    public async Task ReasoningStream_Accumulated()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas(new[] {
            new StreamDelta(null, "think"),
            new StreamDelta(null, "ing"),
            new StreamDelta("answer", null)
        }).Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("thinking", service.Messages[1].Reasoning);
        Assert.Equal("answer", service.Messages[1].Content);
    }

    [Fact]
    public async Task ReasoningOnlyStream_PersistsAssistantWithoutContentPlaceholder()
    {
        // Covers hadRealContent when content is empty but reasoning is present
        // (right-hand arm of the OR) — distinct from EmptyStream_SetsNoResponseContent.
        var (service, _) = _fixture.CreateBuilder().WithDeltas([
            new StreamDelta(null, "think"),
            new StreamDelta(null, "ing")
]).Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Equal(string.Empty, service.Messages[1].Content);
        Assert.Equal("thinking", service.Messages[1].Reasoning);
        Assert.False(service.Messages[1].IsStreaming);
        Assert.Contains(
            service.ApiMessagesForTest,
            m => m.Role == "assistant" && m.Reasoning == "thinking");
    }

    [Fact]
    public async Task CancelledToken_FinalizesThenThrowsOperationCanceled()
    {
        var (service, _) = _fixture.CreateBuilder().Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi", cancellationToken: cts.Token)));

        // Service finalizes before rethrowing — display unlocked, marker only.
        Assert.Equal(2, service.Messages.Count);
        Assert.False(service.Messages[1].IsStreaming);
        Assert.Equal("(stopped)", service.Messages[1].Content);
    }

    [Fact]
    public async Task CancelAfterPartialContent_PersistsPartialAssistantAndRethrows()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas([
            new StreamDelta("partial", null),
            new StreamDelta(" more", null)
]).Build();
        using var cts = new CancellationTokenSource();

        await using var enumerator = service
            .SendStreamingAsync("hi", cancellationToken: cts.Token)
            .GetAsyncEnumerator();

        // Placeholder assistant
        Assert.True(await enumerator.MoveNextAsync());
        // First content delta applied
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("partial", enumerator.Current.Content);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await enumerator.MoveNextAsync())
            {
            }
        });

        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("assistant", service.Messages[1].Role);
        Assert.Equal("partial (stopped)", service.Messages[1].Content);
        Assert.False(service.Messages[1].IsStreaming);
        Assert.False(service.Messages[1].IsError);
    }

    [Fact]
    public async Task CancelAfterPartialReasoning_PersistsReasoningAndRethrows()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas([
            new StreamDelta(null, "thinking…"),
            new StreamDelta("answer", null)
]).Build();
        using var cts = new CancellationTokenSource();

        await using var enumerator = service
            .SendStreamingAsync("hi", cancellationToken: cts.Token)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync()); // placeholder
        Assert.True(await enumerator.MoveNextAsync()); // reasoning delta
        Assert.Equal("thinking…", enumerator.Current.Reasoning);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await enumerator.MoveNextAsync())
            {
            }
        });

        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("thinking…", service.Messages[1].Reasoning);
        Assert.False(service.Messages[1].IsStreaming);
    }

    [Fact]
    public async Task CancelBeforeAnyContent_RemovesEmptyAssistantAndRethrows()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas([
            new StreamDelta("never-delivered", null)
]).Build();
        using var cts = new CancellationTokenSource();

        await using var enumerator = service
            .SendStreamingAsync("hi", cancellationToken: cts.Token)
            .GetAsyncEnumerator();

        // Placeholder only — cancel before any content delta is applied.
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(enumerator.Current.IsStreaming);
        Assert.Equal(string.Empty, enumerator.Current.Content);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await enumerator.MoveNextAsync())
            {
            }
        });

        // Empty assistant placeholder replaced with "(stopped)" marker; user turn remains.
        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("user", service.Messages[0].Role);
        Assert.Equal("(stopped)", service.Messages[1].Content);
        Assert.False(service.Messages[1].IsStreaming);
        Assert.False(service.IsStreamActive);
    }

    [Fact]
    public async Task CancelAfterReasoningOnly_PersistsPartialReasoningAndRethrows()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas([
            new StreamDelta(null, "thinking…"),
            new StreamDelta("answer", null)
]).Build();
        using var cts = new CancellationTokenSource();

        await using var enumerator = service
            .SendStreamingAsync("hi", cancellationToken: cts.Token)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync()); // placeholder
        Assert.True(await enumerator.MoveNextAsync()); // reasoning delta
        Assert.Equal("thinking…", enumerator.Current.Reasoning);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await enumerator.MoveNextAsync())
            {
            }
        });

        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("thinking…", service.Messages[1].Reasoning);
        Assert.False(service.Messages[1].IsStreaming);
    }

    [Fact]
    public async Task CancelWithPartialContent_AwaitsAssistantFinalizationBeforeRethrow()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new RecordingConversationWriter(gate);
        var (service, _) = _fixture.CreateBuilder().WithDeltas([
                new StreamDelta("partial answer", null),
                new StreamDelta(" more", null)
]).WithConversationWriter(writer).Build();
        using var cts = new CancellationTokenSource();

        await using var enumerator = service
            .SendStreamingAsync("hi", cancellationToken: cts.Token)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync()); // placeholder
        Assert.True(await enumerator.MoveNextAsync()); // first content delta
        Assert.Equal("partial answer", enumerator.Current.Content);

        cts.Cancel();

        // Next MoveNext enters cancel finalization and blocks on the gate.
        var moveTask = enumerator.MoveNextAsync().AsTask();
        await writer.FinalizationStarted;
        Assert.False(writer.AssistantFinalizedCompleted);
        Assert.False(moveTask.IsCompleted);

        gate.SetResult();
        Assert.True(await moveTask); // stopped assistant yielded after persist
        Assert.True(writer.AssistantFinalizedCompleted);
        Assert.Equal("partial answer (stopped)", enumerator.Current.Content);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await enumerator.MoveNextAsync())
            {
            }
        });

        Assert.Contains("assistant-finalized", writer.Events);
        Assert.False(service.IsStreamActive);
    }

    [Fact]
    public async Task CancelWithPartialContent_PersistFailure_StillRethrowsCancellation()
    {
        var writer = new RecordingConversationWriter(failFinalization: true);
        var (service, _) = _fixture.CreateBuilder().WithDeltas([
                new StreamDelta("partial answer", null),
                new StreamDelta(" more", null)
]).WithConversationWriter(writer).Build();
        using var cts = new CancellationTokenSource();

        await using var enumerator = service
            .SendStreamingAsync("hi", cancellationToken: cts.Token)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("partial answer", enumerator.Current.Content);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await enumerator.MoveNextAsync())
            {
            }
        });

        Assert.Equal("partial answer (stopped)", service.Messages[1].Content);
        Assert.False(service.IsStreamActive);
    }

    [Fact]
    public async Task SendWithImage_AddsMultipartUserMessageToApiTranscript()
    {
        var fake = new FakeOpenRouterClient([new StreamDelta("looks like a cat", null)]);
        var (service, _) = _fixture.CreateBuilder().WithClient(fake).Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync(
            "What is this?",
            "data:image/jpeg;base64,abc123"));

        Assert.NotNull(fake.LastRequest);
        var user = fake.LastRequest!.Messages[1];
        Assert.Equal("user", user.Role);
        Assert.False(user.Content.IsText);
        Assert.Equal(2, user.Content.Parts.Count);
        Assert.Equal("text", user.Content.Parts[0].Type);
        Assert.Equal("What is this?", user.Content.Parts[0].Text);
        Assert.Equal("image_url", user.Content.Parts[1].Type);
        Assert.Equal("data:image/jpeg;base64,abc123", user.Content.Parts[1].ImageUrl!.Url);
        Assert.Equal("data:image/jpeg;base64,abc123", service.Messages[0].ImageDataUrl);
    }

    [Fact]
    public async Task HappyPath_CapturesUsageFromFinalChunk()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas(new[] {
            new StreamDelta("Hello", null),
            new StreamDelta(null, null, new MessageUsage(1200, 340, 0.0041m))
        }).Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        var assistant = service.Messages[1];
        Assert.Equal("Hello", assistant.Content);
        Assert.NotNull(assistant.Usage);
        Assert.Equal(1200, assistant.Usage!.PromptTokens);
        Assert.Equal(340, assistant.Usage.CompletionTokens);
        Assert.Equal(0.0041m, assistant.Usage.Cost);
        Assert.False(assistant.IsStreaming);
    }

    [Fact]
    public async Task UsageWithoutCost_EstimatesFromCatalogPricing()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas(new[] {
            new StreamDelta("Hi", null),
            new StreamDelta(null, null, new MessageUsage(1000, 500, null))
        }).Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        var usage = service.Messages[1].Usage;
        Assert.NotNull(usage);
        // 1000 * 0.0000025 + 500 * 0.00001 = 0.0025 + 0.005 = 0.0075
        Assert.Equal(0.0075m, usage!.Cost);
    }

    [Fact]
    public async Task SendAsync_DoesNotIncludeDeprecatedUsageIncludeFlag()
    {
        var fake = new FakeOpenRouterClient([new StreamDelta("ok", null)]);
        var (service, _) = _fixture.CreateBuilder().WithClient(fake).Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        var json = JsonSerializer.Serialize(fake.LastRequest);
        Assert.DoesNotContain("\"usage\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HappyPath_UsageOnlyDelta_UpdatesAssistant()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas(new[] {
            new StreamDelta(null, null, new MessageUsage(50, 25, 0.001m))
        }).Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Equal(50, service.Messages[1].Usage!.PromptTokens);
    }

    [Fact]
    public async Task ConcurrentSend_WhileStreamActive_ThrowsInvalidOperation()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas([new StreamDelta("hello", null)]).Build();

        await using var enumerator = service.SendStreamingAsync("first").GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(service.IsStreamActive);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("second")));
        Assert.Contains("already in progress", ex.Message, StringComparison.OrdinalIgnoreCase);

        while (await enumerator.MoveNextAsync())
        {
        }

        Assert.False(service.IsStreamActive);
        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("user", service.Messages[0].Role);
        Assert.Equal("first", service.Messages[0].Content);
        Assert.Equal("assistant", service.Messages[1].Role);
        Assert.Equal("hello", service.Messages[1].Content);
    }
    private sealed class RecordingConversationWriter : IActiveConversationWriter
    {
        private readonly TaskCompletionSource? _blockFinalization;
        private readonly bool _failFinalization;
        private readonly TaskCompletionSource _finalizationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingConversationWriter(
            TaskCompletionSource? blockFinalization = null,
            bool failFinalization = false)
        {
            _blockFinalization = blockFinalization;
            _failFinalization = failFinalization;
        }

        public List<string> Events { get; } = [];
        public bool AssistantFinalizedCompleted { get; private set; }
        public Task FinalizationStarted => _finalizationStarted.Task;

        public Task OnUserMessageCommittedAsync(
            string content,
            string modelId,
            string? imageDataUrl = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add("user-committed");
            return Task.CompletedTask;
        }

        public async Task OnAssistantFinalizedAsync(
            string content,
            string? reasoning,
            MessageUsage? usage = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add("assistant-finalized-started");
            _finalizationStarted.TrySetResult();

            if (_failFinalization)
            {
                throw new InvalidOperationException("persist failed");
            }

            if (_blockFinalization is not null)
            {
                await _blockFinalization.Task;
            }

            AssistantFinalizedCompleted = true;
            Events.Add("assistant-finalized");
        }

        public Task OnLastAssistantRemovedAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("assistant-removed");
            return Task.CompletedTask;
        }
    }

}
