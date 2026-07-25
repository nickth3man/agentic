using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests;

// The first entry of the API transcript must reflect the configured system prompt
// (UI override via SystemPromptService, else OpenRouterOptions.SystemPrompt, else
// OpenRouterOptions.DefaultSystemPrompt). Mirrors ChatAgentServiceModelSelectionTests.
public class ChatAgentServiceSystemPromptTests
{
    [Fact]
    public async Task SendAsync_ApiMessages0_UsesOptionsSystemPrompt_WhenNoUiOverride()
    {
        var (service, fake) = BuildService(
            optionsPrompt: "Config prompt from options.",
            uiPrompt: null);

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("Config prompt from options.", FirstSystemContent(fake));
    }

    [Fact]
    public async Task SendAsync_ApiMessages0_UsesUiPrompt_OverOptions()
    {
        var (service, fake) = BuildService(
            optionsPrompt: "Config prompt from options.",
            uiPrompt: "UI override prompt.");

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("UI override prompt.", FirstSystemContent(fake));
    }

    [Fact]
    public async Task SendAsync_ApiMessages0_FallsBackToDefault_WhenOptionsEmptyAndNoUi()
    {
        var (service, fake) = BuildService(
            optionsPrompt: "   ",
            uiPrompt: null);

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal(OpenRouterOptions.DefaultSystemPrompt, FirstSystemContent(fake));
    }

    [Fact]
    public async Task Reset_PicksUpNewUiPrompt_ForNextConversation()
    {
        var (service, fake, systemPrompt) = BuildServiceWithHandle(
            optionsPrompt: OpenRouterOptions.DefaultSystemPrompt,
            uiPrompt: "First prompt.");

        await Consume(service.SendStreamingAsync("hi"));
        Assert.Equal("First prompt.", FirstSystemContent(fake));

        systemPrompt.SetCurrentPromptForTest("Second prompt.");
        service.Reset();

        await Consume(service.SendStreamingAsync("again"));
        Assert.Equal("Second prompt.", FirstSystemContent(fake));
    }

    [Fact]
    public async Task RefreshSystemMessageIfIdle_UpdatesWhenEmpty_NoOpWhenMessagesExist()
    {
        var (service, fake, systemPrompt) = BuildServiceWithHandle(
            optionsPrompt: "Options prompt.",
            uiPrompt: null);

        systemPrompt.SetCurrentPromptForTest("Idle refresh prompt.");
        service.RefreshSystemMessageIfIdle();

        await Consume(service.SendStreamingAsync("hi"));
        Assert.Equal("Idle refresh prompt.", FirstSystemContent(fake));

        systemPrompt.SetCurrentPromptForTest("Should not apply mid-conversation.");
        service.RefreshSystemMessageIfIdle();

        await Consume(service.SendStreamingAsync("second"));
        // Mid-conversation refresh is a no-op: the system entry from the start of
        // this conversation stays "Idle refresh prompt."
        var messages = fake.LastRequest!.Messages;
        Assert.Equal("Idle refresh prompt.", messages[0].TextContent);
        // Empty fake deltas => no assistant API entry: system + user "hi" + user "second".
        Assert.Equal(3, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("Idle refresh prompt.", messages[0].TextContent);
    }

    [Fact]
    public async Task RefreshSystemMessageIfIdle_WhileStreaming_IsNoOp()
    {
        // Covers the `_streamActive` arm of RefreshSystemMessageIfIdle's early return
        // (the `_displayMessages.Count > 0` arm is covered above). Mirror
        // Reset_WhileStreaming: pause after the first MoveNext so _streamActive is true.
        var (service, _, systemPrompt) = BuildServiceWithHandle(
            optionsPrompt: "Original prompt.",
            uiPrompt: null);

        await using var enumerator = service.SendStreamingAsync("hi").GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("Original prompt.", service.GetApiSystemPromptForTest());

        systemPrompt.SetCurrentPromptForTest("Must not apply while streaming.");
        service.RefreshSystemMessageIfIdle();

        Assert.Equal("Original prompt.", service.GetApiSystemPromptForTest());

        while (await enumerator.MoveNextAsync())
        {
            /* drain */
        }
    }

    [Fact]
    public void ApiMessagesForTest_And_GetApiSystemPromptForTest_ExposeLeadingSystemEntry()
    {
        var (service, _, _) = BuildServiceWithHandle(
            optionsPrompt: "Constructor prompt.",
            uiPrompt: null);

        Assert.Single(service.ApiMessagesForTest);
        Assert.Equal("system", service.ApiMessagesForTest[0].Role);
        Assert.Equal("Constructor prompt.", service.GetApiSystemPromptForTest());
    }

    [Fact]
    public void ResolveSystemPrompt_TrimsWhitespace()
    {
        var (service, _, _) = BuildServiceWithHandle(
            optionsPrompt: "  trimmed options  ",
            uiPrompt: null);

        Assert.Equal("trimmed options", service.ResolveSystemPrompt());
    }

    [Fact]
    public void Presets_DefaultHasNullPrompt_MeaningClearOverride()
    {
        var defaults = SystemPromptService.Presets
            .Where(p => p.Name == "Default")
            .ToList();

        Assert.Single(defaults);
        Assert.Null(defaults[0].Prompt);
        Assert.True(defaults[0].ClearsOverride);
        Assert.Contains(SystemPromptService.Presets, p => !p.ClearsOverride);
        Assert.True(SystemPromptService.Presets.Count >= 4);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (ChatAgentService Service, FakeOpenRouterClient Client) BuildService(
        string optionsPrompt,
        string? uiPrompt)
    {
        var (service, client, _) = BuildServiceWithHandle(optionsPrompt, uiPrompt);
        return (service, client);
    }

    private static (ChatAgentService Service, FakeOpenRouterClient Client, SystemPromptService Prompt)
        BuildServiceWithHandle(string optionsPrompt, string? uiPrompt)
    {
        var fake = new FakeOpenRouterClient();
        var options = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = "test-model",
            SystemPrompt = optionsPrompt
        });
        var logger = NullLogger<ChatAgentService>.Instance;
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
                ["tools"])
        ]);

        var js = TestSupport.NewProtectedJSRuntime();
        var storage = new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest(null);
        var systemPrompt = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);
        systemPrompt.SetCurrentPromptForTest(uiPrompt);

        return (new ChatAgentService(fake, options, logger, selection, catalog, systemPrompt, NullActiveConversationWriter.Instance), fake, systemPrompt);
    }

    private static string FirstSystemContent(FakeOpenRouterClient fake)
    {
        Assert.NotNull(fake.LastRequest);
        var first = fake.LastRequest!.Messages[0];
        Assert.Equal("system", first.Role);
        return first.TextContent;
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
