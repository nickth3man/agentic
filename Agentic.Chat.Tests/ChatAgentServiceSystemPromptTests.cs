using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Agentic.Chat.Tests.Fixtures;

namespace Agentic.Chat.Tests;

// The first entry of the API transcript must reflect the configured system prompt
// (UI override via SystemPromptService, else OpenRouterOptions.SystemPrompt, else
// OpenRouterOptions.DefaultSystemPrompt). Mirrors ChatAgentServiceModelSelectionTests.
public class ChatAgentServiceSystemPromptTests : IClassFixture<ChatAgentServiceFixture>
{
    private readonly ChatAgentServiceFixture _fixture;

    public ChatAgentServiceSystemPromptTests(ChatAgentServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendAsync_ApiMessages0_UsesOptionsSystemPrompt_WhenNoUiOverride()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithOptionsPrompt("Config prompt from options.")
            .WithUiPrompt(null)
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("Config prompt from options.", FirstSystemContent(fake));
    }

    [Fact]
    public async Task SendAsync_ApiMessages0_UsesUiPrompt_OverOptions()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithOptionsPrompt("Config prompt from options.")
            .WithUiPrompt("UI override prompt.")
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Equal("UI override prompt.", FirstSystemContent(fake));
    }

    [Fact]
    public async Task SendAsync_ApiMessages0_FallsBackToDefault_WhenOptionsEmptyAndNoUi()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithOptionsPrompt("   ")
            .WithUiPrompt(null)
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Equal(OpenRouterOptions.DefaultSystemPrompt, FirstSystemContent(fake));
    }

    [Fact]
    public async Task Reset_PicksUpNewUiPrompt_ForNextConversation()
    {
        var (service, fake, systemPrompt) = _fixture.CreateBuilder()
            .WithOptionsPrompt(OpenRouterOptions.DefaultSystemPrompt)
            .WithUiPrompt("First prompt.")
            .BuildWithPromptHandle();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));
        Assert.Equal("First prompt.", FirstSystemContent(fake));

        systemPrompt.SetCurrentPromptForTest("Second prompt.");
        service.Reset();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("again"));
        Assert.Equal("Second prompt.", FirstSystemContent(fake));
    }

    [Fact]
    public async Task RefreshSystemMessageIfIdle_UpdatesWhenEmpty_NoOpWhenMessagesExist()
    {
        var (service, fake, systemPrompt) = _fixture.CreateBuilder()
            .WithOptionsPrompt("Options prompt.")
            .WithUiPrompt(null)
            .BuildWithPromptHandle();

        systemPrompt.SetCurrentPromptForTest("Idle refresh prompt.");
        service.RefreshSystemMessageIfIdle();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));
        Assert.Equal("Idle refresh prompt.", FirstSystemContent(fake));

        systemPrompt.SetCurrentPromptForTest("Should not apply mid-conversation.");
        service.RefreshSystemMessageIfIdle();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("second"));
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
        var (service, _, systemPrompt) = _fixture.CreateBuilder()
            .WithOptionsPrompt("Original prompt.")
            .WithUiPrompt(null)
            .BuildWithPromptHandle();

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
        var (service, _, _) = _fixture.CreateBuilder()
            .WithOptionsPrompt("Constructor prompt.")
            .WithUiPrompt(null)
            .BuildWithPromptHandle();

        Assert.Single(service.ApiMessagesForTest);
        Assert.Equal("system", service.ApiMessagesForTest[0].Role);
        Assert.Equal("Constructor prompt.", service.GetApiSystemPromptForTest());
    }

    [Fact]
    public void ResolveSystemPrompt_TrimsWhitespace()
    {
        var (service, _, _) = _fixture.CreateBuilder()
            .WithOptionsPrompt("  trimmed options  ")
            .WithUiPrompt(null)
            .BuildWithPromptHandle();

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

    private static string FirstSystemContent(FakeOpenRouterClient fake)
    {
        Assert.NotNull(fake.LastRequest);
        var first = fake.LastRequest!.Messages[0];
        Assert.Equal("system", first.Role);
        return first.TextContent;
    }
}
