using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests;

public class ChatAgentServiceLoadTranscriptTests
{
    [Fact]
    public void LoadTranscript_RebuildsDisplayAndApiMessages_IncludingReasoning()
    {
        var service = CreateService();

        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "q" },
            new ChatDisplayMessage
            {
                Role = "assistant",
                Content = "a",
                Reasoning = "think"
            }
        ]);

        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("think", service.Messages[1].Reasoning);
    }

    [Fact]
    public void LoadTranscript_AssistantWithoutReasoning_AddsApiMessageWithNullReasoning()
    {
        var service = CreateService();

        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "q" },
            new ChatDisplayMessage
            {
                Role = "assistant",
                Content = "a",
                Reasoning = "   "
            }
        ]);

        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("a", service.Messages[1].Content);
    }

    [Fact]
    public async Task LoadTranscript_ApiOmitsErrorsAndEmptyPlaceholder_OnNextSend()
    {
        var fake = new FakeOpenRouterClient([new StreamDelta("next", null)]);
        var service = CreateService(fake);

        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "u1" },
            new ChatDisplayMessage
            {
                Role = "assistant",
                Content = "(Error 500: boom)",
                IsError = true
            },
            new ChatDisplayMessage { Role = "user", Content = "u2" },
            new ChatDisplayMessage
            {
                Role = "assistant",
                Content = ChatAgentService.EmptyResponsePlaceholder
            },
            new ChatDisplayMessage
            {
                Role = "assistant",
                Content = string.Empty,
                Reasoning = "only reasoning"
            },
            new ChatDisplayMessage { Role = "tool", Content = "ignored" }
        ]);

        await foreach (var _ in service.SendStreamingAsync("u3"))
        {
        }

        Assert.NotNull(fake.LastRequest);
        var roles = fake.LastRequest!.Messages.Select(m => (m.Role, m.TextContent, m.Reasoning)).ToList();
        Assert.Equal("system", roles[0].Role);
        Assert.Equal("user", roles[1].Role);
        Assert.Equal("u1", roles[1].TextContent);
        Assert.Equal("user", roles[2].Role);
        Assert.Equal("u2", roles[2].TextContent);
        Assert.Equal("assistant", roles[3].Role);
        Assert.Equal("only reasoning", roles[3].Reasoning);
        Assert.Equal("user", roles[4].Role);
        Assert.Equal("u3", roles[4].TextContent);
        Assert.DoesNotContain(fake.LastRequest.Messages, m => m.TextContent.Contains("Error", StringComparison.Ordinal));
        Assert.DoesNotContain(fake.LastRequest.Messages, m => m.TextContent.Contains("No response", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadTranscript_WhileStreaming_IsNoOp()
    {
        var service = CreateService(new StreamDelta("x", null));
        await using var enumerator = service.SendStreamingAsync("hi").GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(service.IsStreamActive);

        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "other" }
        ]);

        Assert.Equal(2, service.Messages.Count);
        Assert.Equal("hi", service.Messages[0].Content);

        while (await enumerator.MoveNextAsync())
        {
        }
    }

    [Fact]
    public async Task LoadTranscript_AssistantWithWhitespaceReasoning_StoresNullReasoningInApi()
    {
        var fake = new FakeOpenRouterClient([new StreamDelta("next", null)]);
        var service = CreateService(fake);

        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "q" },
            new ChatDisplayMessage { Role = "assistant", Content = "answer", Reasoning = "   " }
        ]);

        await foreach (var _ in service.SendStreamingAsync("follow"))
        {
        }

        Assert.NotNull(fake.LastRequest);
        var assistant = Assert.Single(
            fake.LastRequest!.Messages,
            m => m.Role == "assistant" && m.TextContent == "answer");
        Assert.Null(assistant.Reasoning);
    }

    [Fact]
    public async Task LoadTranscript_WhitespaceOnlyAssistant_OmittedFromApi()
    {
        var fake = new FakeOpenRouterClient([new StreamDelta("next", null)]);
        var service = CreateService(fake);

        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "q" },
            new ChatDisplayMessage
            {
                Role = "assistant",
                Content = "   ",
                Reasoning = string.Empty
            }
        ]);

        await foreach (var _ in service.SendStreamingAsync("follow-up"))
        {
        }

        Assert.NotNull(fake.LastRequest);
        // LastRequest is the outbound API transcript (before the new assistant is appended).
        Assert.DoesNotContain(fake.LastRequest!.Messages, m => m.Role == "assistant");
        Assert.Equal("follow-up", service.Messages[^2].Content);
        Assert.Equal("next", service.Messages[^1].Content);
    }

    [Fact]
    public void LoadTranscript_Null_Throws()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.LoadTranscript(null!));
    }

    [Fact]
    public void IsStreamActive_FalseWhenIdle()
    {
        Assert.False(CreateService().IsStreamActive);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("hello", true)]
    public void HasApiVisibleContent_WithoutReasoning_DependsOnContent(string content, bool expected)
    {
        Assert.Equal(expected, ChatAgentService.HasApiVisibleContent(content, string.Empty));
        Assert.Equal(expected, ChatAgentService.HasApiVisibleContent(content, "   "));
    }

    [Fact]
    public void HasApiVisibleContent_EmptyPlaceholder_IsNotVisible()
    {
        Assert.False(ChatAgentService.HasApiVisibleContent(
            ChatAgentService.EmptyResponsePlaceholder,
            string.Empty));
    }

    [Fact]
    public void HasApiVisibleContent_WithReasoning_IsTrueEvenIfContentEmpty()
    {
        Assert.True(ChatAgentService.HasApiVisibleContent(string.Empty, "thoughts"));
        Assert.True(ChatAgentService.HasApiVisibleContent(
            ChatAgentService.EmptyResponsePlaceholder,
            "thoughts"));
    }

    [Fact]
    public async Task LoadTranscript_AssistantWithNonEmptyReasoning_PassesReasoningToApi()
    {
        var fake = new FakeOpenRouterClient([new StreamDelta("next", null)]);
        var service = CreateService(fake);

        service.LoadTranscript(
        [
            new ChatDisplayMessage { Role = "user", Content = "q" },
            new ChatDisplayMessage
            {
                Role = "assistant",
                Content = "answer",
                Reasoning = "detailed thought"
            }
        ]);

        await foreach (var _ in service.SendStreamingAsync("follow-up"))
        {
        }

        Assert.NotNull(fake.LastRequest);
        var assistant = Assert.Single(
            fake.LastRequest!.Messages,
            m => m.Role == "assistant" && m.TextContent == "answer");
        Assert.Equal("detailed thought", assistant.Reasoning);
    }

    [Fact]
    public void LoadTranscript_UserWithImage_RebuildsMultipartApiMessage()
    {
        var service = CreateService(new FakeOpenRouterClient([]));
        service.LoadTranscript(
        [
            new ChatDisplayMessage
            {
                Role = "user",
                Content = "look",
                ImageDataUrl = "data:image/jpeg;base64,abc"
            }
        ]);

        var user = service.ApiMessagesForTest[1];
        Assert.Equal("user", user.Role);
        Assert.False(user.Content.IsText);
        Assert.Equal("look", user.Content.Parts[0].Text);
        Assert.Equal("image_url", user.Content.Parts[1].Type);
        Assert.Equal("data:image/jpeg;base64,abc", user.Content.Parts[1].ImageUrl!.Url);
    }

    private static ChatAgentService CreateService(params StreamDelta[] deltas)
        => CreateService(new FakeOpenRouterClient(deltas));

    private static ChatAgentService CreateService(FakeOpenRouterClient fake)
    {
        var options = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = "test-model"
        });
        var catalog = new ModelCatalogService(new UnusedHttpClientFactory());
        catalog.SeedForTest(
        [
            new OpenRouterModel(
                "test-model",
                "test-model",
                128_000L,
                DateTimeOffset.UtcNow,
                "text->text",
                new OpenRouterPricing(0.0000025m, 0.00001m),
                ["tools", "reasoning"])
        ]);
        var js = TestSupport.NewProtectedJSRuntime();
        var storage = new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest(null);
        var systemPrompt = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);
        systemPrompt.SetCurrentPromptForTest(null);
        return new ChatAgentService(
            fake,
            options,
            NullLogger<ChatAgentService>.Instance,
            selection,
            catalog,
            systemPrompt,
            NullActiveConversationWriter.Instance);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("Catalog must not fetch in this test.");
    }
}
