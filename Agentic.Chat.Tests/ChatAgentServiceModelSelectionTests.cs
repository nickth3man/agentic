using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests;

// Tests for ChatAgentService's per-call model selection. The body-shaping change
// moved from a hardcoded `reasoning` key plus a fixed model id (read from
// OpenRouterOptions) to runtime composition:
//   - Model id: SelectedModelService.CurrentModelId ?? OpenRouterOptions.Model
//   - reasoning key: included only when the catalog's FindByIdAsync resolves the
//     id AND the resolved model has SupportsReasoning == true.
public class ChatAgentServiceModelSelectionTests
{
    private const string DefaultModel = "openai/gpt-oss-120b";

    [Fact]
    public async Task SendAsync_UsesSelectedModelId_WhenSet()
    {
        var (service, fake) = BuildService(
            selectedModelId: "anthropic/claude-3.5-sonnet",
            catalogModels: new[] { ("anthropic/claude-3.5-sonnet", true) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Equal("anthropic/claude-3.5-sonnet", fake.LastRequest!.Model);
    }

    [Fact]
    public async Task SendAsync_FallsBackToOptionsModel_WhenSelectionNotLoaded()
    {
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Equal(DefaultModel, fake.LastRequest!.Model);
    }

    [Fact]
    public async Task SendAsync_IncludesReasoning_WhenModelSupportsIt()
    {
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.NotNull(fake.LastRequest!.Reasoning);
        Assert.Equal("medium", fake.LastRequest.Reasoning!.Effort);
        Assert.False(fake.LastRequest.Reasoning.Exclude);
    }

    [Fact]
    public async Task SendAsync_OmitsReasoning_WhenEffortOff()
    {
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) },
            effort: ReasoningEffortLevel.Off);

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Null(fake.LastRequest!.Reasoning);
    }

    [Theory]
    [InlineData(ReasoningEffortLevel.Low, "low")]
    [InlineData(ReasoningEffortLevel.Medium, "medium")]
    [InlineData(ReasoningEffortLevel.High, "high")]
    public async Task SendAsync_SerializesEffortLevel(ReasoningEffortLevel effort, string expected)
    {
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) },
            effort: effort);

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest!.Reasoning);
        Assert.Equal(expected, fake.LastRequest.Reasoning!.Effort);
    }

    [Fact]
    public async Task SendAsync_IncludesTemperature_WhenSupported()
    {
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) },
            effort: ReasoningEffortLevel.Medium,
            temperature: 0.4,
            maxTokens: 512,
            extraParameters: ["temperature", "max_tokens"]);

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Equal(0.4, fake.LastRequest!.Temperature);
        Assert.Equal(512, fake.LastRequest.MaxTokens);
    }

    [Fact]
    public async Task SendAsync_OmitsTemperature_WhenNotInSupportedParameters()
    {
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) },
            effort: ReasoningEffortLevel.Medium,
            temperature: 0.4,
            maxTokens: 512,
            extraParameters: []);

        await Consume(service.SendStreamingAsync("hi"));

        Assert.Null(fake.LastRequest!.Temperature);
        Assert.Null(fake.LastRequest.MaxTokens);
    }

    [Fact]
    public async Task SendAsync_OmitsReasoning_WhenModelDoesNotSupportIt()
    {
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, false) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Null(fake.LastRequest!.Reasoning);
    }

    [Fact]
    public async Task SendAsync_OmitsReasoning_WhenModelNotFoundInCatalog()
    {
        // Catalog returns null for the requested id (e.g. fallback default that isn't
        // present in the seeded list), so reasoning must NOT be included.
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: Array.Empty<(string, bool)>());

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Null(fake.LastRequest!.Reasoning);
        Assert.Equal(DefaultModel, fake.LastRequest.Model);
    }

    [Fact]
    public async Task SendAsync_SelectedModelTakesPrecedenceOverCatalogFallback()
    {
        // Even if the catalog does not know about the selected id, we honor the
        // user's selection — the request still uses that id, just without reasoning.
        var (service, fake) = BuildService(
            selectedModelId: "newvendor/unknown-experimental",
            catalogModels: new[] { (DefaultModel, true) });

        await Consume(service.SendStreamingAsync("hi"));

        Assert.NotNull(fake.LastRequest);
        Assert.Equal("newvendor/unknown-experimental", fake.LastRequest!.Model);
        Assert.Null(fake.LastRequest.Reasoning);
    }

    [Fact]
    public void GetContextWindow_UsesCompleteApiTranscriptForMeter()
    {
        var (service, _) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) });

        var context = service.GetContextWindow(128_000);

        Assert.Single(context.Messages);
        Assert.True(context.TranscriptTokens > 0);
        Assert.Equal(0, context.ExcludedMessageCount);
    }

    [Fact]
    public async Task SendAsync_UsesTrimmedContextWithoutChangingDisplayTranscript()
    {
        var (service, fake) = BuildService(
            selectedModelId: null,
            catalogModels: new[] { (DefaultModel, true) },
            contextLength: 100);
        var oldUser = new string('a', 160);
        var recentUser = new string('b', 160);

        await Consume(service.SendStreamingAsync(oldUser));
        await Consume(service.SendStreamingAsync(recentUser));
        await Consume(service.SendStreamingAsync(new string('c', 160)));

        Assert.NotNull(fake.LastRequest);
        Assert.DoesNotContain(fake.LastRequest!.Messages, message => message.Content == oldUser);
        Assert.Equal(6, service.Messages.Count);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (ChatAgentService Service, FakeOpenRouterClient Client) BuildService(
        string? selectedModelId,
        (string Id, bool SupportsReasoning)[] catalogModels,
        long contextLength = 128_000,
        ReasoningEffortLevel effort = ReasoningEffortLevel.Medium,
        double? temperature = null,
        int? maxTokens = null,
        string[]? extraParameters = null)
    {
        var fake = new FakeOpenRouterClient();
        var options = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = DefaultModel
        });
        var logger = NullLogger<ChatAgentService>.Instance;

        var catalog = new ModelCatalogService(new UnusedHttpClientFactory());
        // Always seed (even with an empty list) so FindByIdAsync returns null on a
        // populated but empty cache, instead of triggering a real /models fetch
        // through the test's HTTP handler.
        catalog.SeedForTest(
            catalogModels.Select(m =>
            {
                var parameters = new List<string> { "tools" };
                if (m.SupportsReasoning)
                {
                    parameters.Add("reasoning");
                }

                if (extraParameters is not null)
                {
                    parameters.AddRange(extraParameters);
                }

                return new OpenRouterModel(
                    m.Id,
                    m.Id,
                    contextLength,
                    DateTimeOffset.UtcNow,
                    "text->text",
                    new OpenRouterPricing(0m, 0m),
                    parameters);
            })
                .ToList());

        var js = TestSupport.NewProtectedJSRuntime();
        var storage = new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider());
        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest(selectedModelId);
        var systemPrompt = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);
        systemPrompt.SetCurrentPromptForTest(null);
        var chatSettings = TestSupport.NewChatSettings(storage);
        chatSettings.SetForTest(effort, temperature, maxTokens);

        return (new ChatAgentService(fake, options, logger, selection, catalog, systemPrompt, chatSettings, NullActiveConversationWriter.Instance), fake);
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


