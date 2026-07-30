using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Agentic.Chat.Tests.Fixtures;

namespace Agentic.Chat.Tests;

// Tests for ChatAgentService's per-call model selection. The body-shaping change
// moved from a hardcoded `reasoning` key plus a fixed model id (read from
// OpenRouterOptions) to runtime composition:
//   - Model id: SelectedModelService.CurrentModelId ?? OpenRouterOptions.Model
//   - reasoning key: included only when the catalog's FindByIdAsync resolves the
//     id AND the resolved model has SupportsReasoning == true.
public class ChatAgentServiceModelSelectionTests : IClassFixture<ChatAgentServiceFixture>
{
    private const string DefaultModel = "test-model";

    private readonly ChatAgentServiceFixture _fixture;

    public ChatAgentServiceModelSelectionTests(ChatAgentServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendAsync_UsesSelectedModelId_WhenSet()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithSelectedModelId("anthropic/claude-3.5-sonnet")
            .WithCatalogModel("anthropic/claude-3.5-sonnet", true)
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Equal("anthropic/claude-3.5-sonnet", fake.LastRequest!.Model);
    }

    [Fact]
    public async Task SendAsync_FallsBackToOptionsModel_WhenSelectionNotLoaded()
    {
        var (service, fake) = _fixture.CreateBuilder().Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Equal(DefaultModel, fake.LastRequest!.Model);
    }

    [Fact]
    public async Task SendAsync_IncludesReasoning_WhenModelSupportsIt()
    {
        var (service, fake) = _fixture.CreateBuilder().Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.NotNull(fake.LastRequest!.Reasoning);
        Assert.Equal("medium", fake.LastRequest.Reasoning!.Effort);
        Assert.False(fake.LastRequest.Reasoning.Exclude);
    }

    [Fact]
    public async Task SendAsync_OmitsReasoning_WhenEffortOff()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithReasoning(ReasoningEffortLevel.Off)
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Null(fake.LastRequest!.Reasoning);
    }

    [Theory]
    [InlineData(ReasoningEffortLevel.Low, "low")]
    [InlineData(ReasoningEffortLevel.Medium, "medium")]
    [InlineData(ReasoningEffortLevel.High, "high")]
    public async Task SendAsync_SerializesEffortLevel(ReasoningEffortLevel effort, string expected)
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithReasoning(effort)
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest!.Reasoning);
        Assert.Equal(expected, fake.LastRequest.Reasoning!.Effort);
    }

    [Fact]
    public async Task SendAsync_IncludesTemperature_WhenSupported()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithReasoning(ReasoningEffortLevel.Medium)
            .WithTemperature(0.4)
            .WithMaxTokens(512)
            .WithCatalogModel(DefaultModel, true, extraParameters: ["temperature", "max_tokens"])
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Equal(0.4, fake.LastRequest!.Temperature);
        Assert.Equal(512, fake.LastRequest.MaxTokens);
    }

    [Fact]
    public async Task SendAsync_OmitsTemperature_WhenNotInSupportedParameters()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithReasoning(ReasoningEffortLevel.Medium)
            .WithTemperature(0.4)
            .WithMaxTokens(512)
            .WithCatalogModel(DefaultModel, true)
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.Null(fake.LastRequest!.Temperature);
        Assert.Null(fake.LastRequest.MaxTokens);
    }

    [Fact]
    public async Task SendAsync_OmitsReasoning_WhenModelDoesNotSupportIt()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithCatalogModel(DefaultModel, false)
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Null(fake.LastRequest!.Reasoning);
    }

    [Fact]
    public async Task SendAsync_OmitsReasoning_WhenModelNotFoundInCatalog()
    {
        // Catalog returns null for the requested id (e.g. fallback default that isn't
        // present in the seeded list), so reasoning must NOT be included.
        var (service, fake) = _fixture.CreateBuilder()
            .WithoutDefaultCatalog()
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Null(fake.LastRequest!.Reasoning);
        Assert.Equal(DefaultModel, fake.LastRequest.Model);
    }

    [Fact]
    public async Task SendAsync_SelectedModelTakesPrecedenceOverCatalogFallback()
    {
        // Even if the catalog does not know about the selected id, we honor the
        // user's selection — the request still uses that id, just without reasoning.
        var (service, fake) = _fixture.CreateBuilder()
            .WithSelectedModelId("newvendor/unknown-experimental")
            .WithCatalogModel(DefaultModel, true)
            .Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Equal("newvendor/unknown-experimental", fake.LastRequest!.Model);
        Assert.Null(fake.LastRequest.Reasoning);
    }

    [Fact]
    public void GetContextWindow_UsesCompleteApiTranscriptForMeter()
    {
        var (service, _) = _fixture.CreateBuilder().Build();

        var context = service.GetContextWindow(128_000);

        Assert.Single(context.Messages);
        Assert.True(context.TranscriptTokens > 0);
        Assert.Equal(0, context.ExcludedMessageCount);
    }

    [Fact]
    public async Task SendAsync_UsesTrimmedContextWithoutChangingDisplayTranscript()
    {
        var (service, fake) = _fixture.CreateBuilder()
            .WithCatalogModel(DefaultModel, true, contextLength: 100)
            .Build();
        var oldUser = new string('a', 160);
        var recentUser = new string('b', 160);

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync(oldUser));
        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync(recentUser));
        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync(new string('c', 160)));

        Assert.NotNull(fake.LastRequest);
        Assert.DoesNotContain(fake.LastRequest!.Messages, message => message.Content == oldUser);
        Assert.Equal(6, service.Messages.Count);
    }

    [Fact]
    public void BuildCompletionRequest_OmitsParams_WhenModelInfoNull()
    {
        var request = ChatAgentService.BuildCompletionRequest(
            "m",
            Array.Empty<ApiChatMessage>(),
            modelInfo: null,
            ReasoningEffortLevel.High,
            temperature: 0.5,
            maxTokens: 100);

        Assert.Null(request.Reasoning);
        Assert.Null(request.Temperature);
        Assert.Null(request.MaxTokens);
    }

    [Fact]
    public void BuildCompletionRequest_SendsMaxTokens_WhenSupported_EvenIfTemperatureNull()
    {
        var model = new OpenRouterModel(
            "m",
            "m",
            1000,
            DateTimeOffset.UtcNow,
            "text->text",
            new OpenRouterPricing(0m, 0m),
            ["max_tokens"]);

        var request = ChatAgentService.BuildCompletionRequest(
            "m",
            Array.Empty<ApiChatMessage>(),
            model,
            ReasoningEffortLevel.Off,
            temperature: null,
            maxTokens: 50);

        Assert.Null(request.Temperature);
        Assert.Equal(50, request.MaxTokens);
    }
}
