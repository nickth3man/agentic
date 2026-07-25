using System.Text.Json;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests;

// Tests for issue #15 — the error/placeholder leak bug and the new Retry/Regenerate
// affordances. Transcript invariant under test: error placeholders and the
// "(No response content returned.)" placeholder are NEVER appended to _apiMessages;
// only real user and assistant turns live there.
public class ChatAgentServiceTranscriptHygieneTests
{
    // Cached JsonSerializerOptions satisfies CA1869 (cached for performance) and
    // matches the JSON serialization shape used by ChatAgentServiceResetTests so
    // the request shape assertions here are stable.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task EmptyResponse_PlaceholderNotAddedToTranscript()
    {
        // Two empty-stream sends in a row. Neither should leave a "(No response
        // content returned.)" placeholder in the API transcript — the second
        // request's messages must be exactly [system, user, user] with no
        // placeholder strings.
        var (service, fake) = CreateService();

        await Consume(service.SendStreamingAsync("first"));
        await Consume(service.SendStreamingAsync("second"));

        Assert.NotNull(fake.LastRequest);
        var messages = fake.LastRequest!.Messages;
        Assert.Equal(3, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("first", messages[1].Content);
        Assert.Equal("user", messages[2].Role);
        Assert.Equal("second", messages[2].Content);

        // Cross-check via JSON serialization to guard the wire shape as well —
        // no message in the serialized body should contain the placeholder string.
        var serialized = JsonSerializer.Serialize(fake.LastRequest, JsonOptions);
        Assert.DoesNotContain("(No response content returned.)", serialized);
    }

    [Fact]
    public async Task ErrorResponse_ErrorTextNotAddedToTranscript()
    {
        // First send errors with a 429 + body. The error assistant should be
        // visible in the display list (with IsError=true), but it must NEVER
        // appear in the next request's transcript. Second send uses an empty
        // fake so we can assert the transcript shape cleanly.
        var (service, fake) = CreateService(
            FakeOpenRouterClient.FakeResponse.Err(new OpenRouterException(429, "too many requests")),
            FakeOpenRouterClient.FakeResponse.Ok());

        await Consume(service.SendStreamingAsync("hi"));

        var errorAssistant = service.Messages[^1];
        Assert.Equal("assistant", errorAssistant.Role);
        Assert.True(errorAssistant.IsError);
        Assert.Contains("too many requests", errorAssistant.Content);

        await Consume(service.SendStreamingAsync("again"));

        Assert.NotNull(fake.LastRequest);
        var messages = fake.LastRequest!.Messages;
        Assert.Equal(3, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("hi", messages[1].Content);
        Assert.Equal("user", messages[2].Role);
        Assert.Equal("again", messages[2].Content);

        var serialized = JsonSerializer.Serialize(fake.LastRequest, JsonOptions);
        Assert.DoesNotContain("Error", serialized);
        Assert.DoesNotContain("too many requests", serialized);
    }

    [Fact]
    public async Task RetryLastAsync_AfterError_ReplacesErrorAndRestreams()
    {
        var (service, fake) = CreateService(
            FakeOpenRouterClient.FakeResponse.Err(new OpenRouterException(500, "boom")),
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("Hello", null)));

        await Consume(service.SendStreamingAsync("hi"));
        Assert.True(service.Messages[^1].IsError);

        await Consume(service.RetryLastAsync());

        // The error placeholder is gone, replaced by the new assistant turn.
        var assistant = service.Messages[^1];
        Assert.Equal("assistant", assistant.Role);
        Assert.False(assistant.IsError);
        Assert.False(assistant.IsStreaming);
        Assert.Equal("Hello", assistant.Content);

        // Two calls total: initial send (errored) + retry (succeeded).
        Assert.Equal(2, fake.CallCount);

        // The retry's request messages must have had the user turn exactly once
        // (no compounding from the retry path).
        Assert.NotNull(fake.LastRequest);
        var messages = fake.LastRequest!.Messages;
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("hi", messages[1].Content);
    }

    [Fact]
    public async Task RetryLastAsync_WhenLastIsNotError_IsNoOp()
    {
        // After a normal successful send, RetryLastAsync must be a no-op:
        // Messages unchanged and the fake is never re-entered.
        var (service, fake) = CreateService(
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("Hi", null)),
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("Should not happen", null)));

        await Consume(service.SendStreamingAsync("hi"));
        var messagesAfterSend = service.Messages.ToArray();

        await Consume(service.RetryLastAsync());

        Assert.Equal(messagesAfterSend.Length, service.Messages.Count);
        Assert.Same(messagesAfterSend[^1], service.Messages[^1]);
        Assert.Equal("Hi", service.Messages[^1].Content);
        Assert.False(service.Messages[^1].IsError);
        // Initial send consumed exactly one scripted response; retry's no-op
        // path must NOT have called the fake.
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task RegenerateAsync_ReplacesLastAssistant()
    {
        // First send -> "first". Regenerate -> "second". Assert the second
        // call's request saw the user turn only (the prior assistant was popped
        // from the transcript before re-streaming). Then send a third message
        // and confirm the regenerated assistant is in the transcript exactly once.
        var (service, fake) = CreateService(
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("first", null)),
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("second", null)),
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("ok", null)));

        await Consume(service.SendStreamingAsync("hi"));
        Assert.Equal("first", service.Messages[^1].Content);

        await Consume(service.RegenerateAsync());
        Assert.Equal("second", service.Messages[^1].Content);
        // The display message count is unchanged from after the first send: the
        // assistant was replaced, the user turn was kept.
        Assert.Equal(2, service.Messages.Count);

        // The regenerate call's request had only [system, user] — the prior
        // assistant was popped from the transcript.
        Assert.NotNull(fake.LastRequest);
        var regenerateMessages = fake.LastRequest!.Messages;
        Assert.Equal(2, regenerateMessages.Count);
        Assert.Equal("system", regenerateMessages[0].Role);
        Assert.Equal("user", regenerateMessages[1].Role);
        Assert.Equal("hi", regenerateMessages[1].Content);

        await Consume(service.SendStreamingAsync("more"));

        // Third call's transcript: system + user + regenerated assistant + new user.
        var messages = fake.LastRequest!.Messages;
        Assert.Equal(4, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("hi", messages[1].Content);
        Assert.Equal("assistant", messages[2].Role);
        Assert.Equal("second", messages[2].Content);
        Assert.Equal("user", messages[3].Role);
        Assert.Equal("more", messages[3].Content);
    }

    [Fact]
    public async Task RegenerateAsync_AfterEmptyResponse_KeepsUserAndRestreams()
    {
        // First send yields nothing -> a display-only "(No response content...)"
        // placeholder that was NOT added to _apiMessages. Regenerate must pop that
        // placeholder from display, leave the user turn intact in the transcript
        // (the _apiMessages[^1].Role == "assistant" guard must refuse to pop the
        // user message), and stream a fresh response.
        var (service, fake) = CreateService(
            FakeOpenRouterClient.FakeResponse.Ok(),
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("real answer", null)));

        await Consume(service.SendStreamingAsync("hi"));
        Assert.Equal("(No response content returned.)", service.Messages[^1].Content);

        await Consume(service.RegenerateAsync());

        var assistant = service.Messages[^1];
        Assert.Equal("assistant", assistant.Role);
        Assert.False(assistant.IsError);
        Assert.Equal("real answer", assistant.Content);

        // The regenerate request must have seen [system, user] — the user turn was
        // NOT removed even though the placeholder assistant had no transcript entry.
        Assert.NotNull(fake.LastRequest);
        var messages = fake.LastRequest!.Messages;
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("hi", messages[1].Content);

        // A follow-up send proves the regenerated assistant entered the transcript once.
        await Consume(service.SendStreamingAsync("more"));
        var follow = fake.LastRequest!.Messages;
        Assert.Equal(4, follow.Count);
        Assert.Equal("assistant", follow[2].Role);
        Assert.Equal("real answer", follow[2].Content);
    }

    [Fact]
    public async Task RegenerateAsync_WhenLastIsUser_IsNoOp()
    {
        // After a send that errored, the last display message is an error
        // assistant, not a completed assistant. Regenerate must NOT touch it.
        var (service, fake) = CreateService(
            FakeOpenRouterClient.FakeResponse.Err(new OpenRouterException(500, "boom")),
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("Should not happen", null)));

        await Consume(service.SendStreamingAsync("hi"));
        var snapshot = service.Messages.ToArray();

        await Consume(service.RegenerateAsync());

        Assert.Equal(snapshot.Length, service.Messages.Count);
        Assert.Same(snapshot[^1], service.Messages[^1]);
        Assert.True(service.Messages[^1].IsError);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task RegenerateAsync_WhenEmpty_IsNoOp()
    {
        var (service, fake) = CreateService(
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("Should not happen", null)));

        Assert.Empty(service.Messages);

        await Consume(service.RegenerateAsync());

        Assert.Empty(service.Messages);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task RetryLastAsync_WhenEmpty_IsNoOp()
    {
        var (service, fake) = CreateService(
            FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("Should not happen", null)));

        Assert.Empty(service.Messages);

        await Consume(service.RetryLastAsync());

        Assert.Empty(service.Messages);
        Assert.Equal(0, fake.CallCount);
    }

    // Defensive-branch coverage. The helpers are internal so these tests can
    // invoke them directly to reach states that aren't reachable through the
    // public API (e.g. last display message is a user — the public APIs always
    // pair user with assistant, so a trailing user only arises when the helpers
    // are called outside the normal send/retry/regenerate lifecycle, or via
    // AddDisplayMessageForTest for tests that need a specific display shape).

    [Fact]
    public void TryPopErrorPlaceholder_DisplayEmpty_ReturnsFalse()
    {
        var (service, _) = CreateService();
        Assert.Empty(service.Messages);

        Assert.False(service.TryPopErrorPlaceholder());
    }

    [Fact]
    public void TryPopErrorPlaceholder_LastIsUser_ReturnsFalse()
    {
        var (service, _) = CreateService();
        service.AddDisplayMessageForTest("user", "hi");

        Assert.False(service.TryPopErrorPlaceholder());
        // Display untouched.
        Assert.Single(service.Messages);
    }

    [Fact]
    public void TryPopErrorPlaceholder_LastIsStreamingAssistant_ReturnsFalse()
    {
        var (service, _) = CreateService();
        service.AddDisplayMessageForTest("assistant", "streaming");

        Assert.False(service.TryPopErrorPlaceholder());
        Assert.Single(service.Messages);
    }

    [Fact]
    public void TryPopErrorPlaceholder_PopsErrorAssistant()
    {
        var (service, _) = CreateService();
        service.AddDisplayMessageForTest("user", "hi");
        // Simulate an error assistant in display without involving the streaming
        // core (which would normally set IsError via an OpenRouterException).
        var errorAssistant = new ChatDisplayMessage
        {
            Role = "assistant",
            Content = "(Error 500: boom)",
            IsError = true
        };
        // Bypass AddDisplayMessageForTest here because we need IsError=true.
        var field = typeof(ChatAgentService)
            .GetField("_displayMessages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((List<ChatDisplayMessage>)field.GetValue(service)!).Add(errorAssistant);

        Assert.True(service.TryPopErrorPlaceholder());
        Assert.Single(service.Messages);
        Assert.Equal("user", service.Messages[0].Role);
    }

    [Fact]
    public void TryPopLastCompletedAssistant_DisplayEmpty_ReturnsFalse()
    {
        var (service, _) = CreateService();
        Assert.Empty(service.Messages);

        Assert.False(service.TryPopLastCompletedAssistant(out _));
    }

    [Fact]
    public void TryPopLastCompletedAssistant_LastIsUser_ReturnsFalse()
    {
        var (service, _) = CreateService();
        service.AddDisplayMessageForTest("user", "hi");

        Assert.False(service.TryPopLastCompletedAssistant(out _));
        Assert.Single(service.Messages);
    }

    [Fact]
    public async Task TryPopLastCompletedAssistant_LastIsStreamingAssistant_ReturnsFalse()
    {
        // After the first MoveNextAsync, the streaming core has added both the
        // user and the streaming assistant placeholder. Pop should refuse
        // because IsStreaming is true.
        var (service, _) = CreateService(FakeOpenRouterClient.FakeResponse.Ok(new StreamDelta("Hi", null)));
        await using var enumerator = service.SendStreamingAsync("hi").GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, service.Messages.Count);
        Assert.True(service.Messages[^1].IsStreaming);

        Assert.False(service.TryPopLastCompletedAssistant(out _));
        Assert.Equal(2, service.Messages.Count);
    }

    [Fact]
    public void TryPopLastCompletedAssistant_RealContent_WasPersistedTrue()
    {
        var (service, _) = CreateService();
        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "q" },
            new ChatDisplayMessage { Role = "assistant", Content = "answer", Reasoning = "think" }
        ]);

        Assert.Equal(2, service.Messages.Count);
        Assert.True(service.TryPopLastCompletedAssistant(out var wasPersisted));
        Assert.True(wasPersisted);
        Assert.Single(service.Messages);
    }

    [Fact]
    public void TryPopLastCompletedAssistant_NullReasoning_WasPersistedByContent()
    {
        var (service, _) = CreateService();
        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "q" },
            new ChatDisplayMessage { Role = "assistant", Content = "answer", Reasoning = null! }
        ]);

        Assert.Equal(2, service.Messages.Count);
        Assert.True(service.TryPopLastCompletedAssistant(out var wasPersisted));
        Assert.True(wasPersisted);
    }

    [Fact]
    public void Constructor_NullConversationWriter_ThrowsArgumentNullException()
    {
        var options = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = "test-model"
        });
        var catalog = new ModelCatalogService(new UnusedHttpClientFactory());
        var js = TestSupport.NewProtectedJSRuntime();
        var storage = new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ChatAgentService(
                new FakeOpenRouterClient(),
                options,
                NullLogger<ChatAgentService>.Instance,
                selection,
                catalog,
                null!));
        Assert.Equal("conversationWriter", ex.ParamName);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (ChatAgentService Service, FakeOpenRouterClient Client) CreateService(
        params FakeOpenRouterClient.FakeResponse[] responses)
    {
        var fake = new FakeOpenRouterClient(responses);
        var options = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = "test-model"
        });
        var logger = NullLogger<ChatAgentService>.Instance;
        var catalog = new ModelCatalogService(new UnusedHttpClientFactory());
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

        return (new ChatAgentService(
            fake, options, logger, selection, catalog, NullActiveConversationWriter.Instance), fake);
    }

    private static async Task Consume(IAsyncEnumerable<ChatDisplayMessage> stream)
    {
        await foreach (var _ in stream)
        {
            /* drain */
        }
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("The seeded model catalog must not fetch models in this test.");
    }
}


