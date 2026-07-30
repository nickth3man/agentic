using System.Text.Json;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests.Fixtures;

/// <summary>
/// Fluent builder for constructing a <see cref="ChatAgentService"/> in unit tests.
/// Replaces the copy-pasted CreateService / BuildService helpers across the
/// ChatAgentService*Tests files.
/// </summary>
public sealed class ChatAgentServiceBuilder
{
    private readonly ProtectedBrowserStorageFixture _storageFixture;
    private readonly HttpMessageHandlerFixture _httpFixture;

    private FakeOpenRouterClient? _client;
    private StreamDelta[]? _deltas;
    private Exception? _exception;
    private string? _optionsPrompt;
    private string? _uiPrompt;
    private string? _selectedModelId;
    private readonly List<OpenRouterModel> _catalogModels = [];
    private bool _seedDefaultCatalog = true;
    private ReasoningEffortLevel _reasoning = ReasoningEffortLevel.Medium;
    private double? _temperature;
    private int? _maxTokens;
    private IActiveConversationWriter? _conversationWriter;

    public ChatAgentServiceBuilder(
        ProtectedBrowserStorageFixture storageFixture,
        HttpMessageHandlerFixture httpFixture)
    {
        _storageFixture = storageFixture;
        _httpFixture = httpFixture;
    }

    public ChatAgentServiceBuilder WithClient(FakeOpenRouterClient client)
    {
        _client = client;
        _deltas = null;
        _exception = null;
        return this;
    }

    public ChatAgentServiceBuilder WithDeltas(params StreamDelta[] deltas)
    {
        _deltas = deltas;
        _exception = null;
        _client = null;
        return this;
    }

    public ChatAgentServiceBuilder WithResponses(params FakeOpenRouterClient.FakeResponse[] responses)
    {
        _client = responses.Length == 0 ? new FakeOpenRouterClient() : new FakeOpenRouterClient(responses);
        _deltas = null;
        _exception = null;
        return this;
    }

    public ChatAgentServiceBuilder WithException(Exception exception)
    {
        _exception = exception;
        _deltas = null;
        _client = null;
        return this;
    }

    public ChatAgentServiceBuilder WithOptionsPrompt(string? prompt)
    {
        _optionsPrompt = prompt;
        return this;
    }

    public ChatAgentServiceBuilder WithUiPrompt(string? prompt)
    {
        _uiPrompt = prompt;
        return this;
    }

    public ChatAgentServiceBuilder WithSelectedModelId(string? id)
    {
        _selectedModelId = id;
        return this;
    }

    public ChatAgentServiceBuilder WithCatalogModel(
        string id,
        bool supportsReasoning,
        long contextLength = 128_000L,
        params string[] extraParameters)
    {
        var parameters = new List<string> { "tools" };
        if (supportsReasoning)
        {
            parameters.Add("reasoning");
        }

        parameters.AddRange(extraParameters);
        _catalogModels.Add(new OpenRouterModel(
            id,
            id,
            contextLength,
            DateTimeOffset.UtcNow,
            "text->text",
            new OpenRouterPricing(0m, 0m),
            parameters.ToArray()));
        _seedDefaultCatalog = false;
        return this;
    }

    public ChatAgentServiceBuilder WithReasoning(ReasoningEffortLevel effort)
    {
        _reasoning = effort;
        return this;
    }

    public ChatAgentServiceBuilder WithTemperature(double? temperature)
    {
        _temperature = temperature;
        return this;
    }

    public ChatAgentServiceBuilder WithMaxTokens(int? maxTokens)
    {
        _maxTokens = maxTokens;
        return this;
    }

    public ChatAgentServiceBuilder WithConversationWriter(IActiveConversationWriter writer)
    {
        _conversationWriter = writer;
        return this;
    }

    public ChatAgentServiceBuilder WithoutDefaultCatalog()
    {
        _seedDefaultCatalog = false;
        return this;
    }

    public (ChatAgentService Service, FakeOpenRouterClient Client) Build()
    {
        var (service, client, _) = BuildInternal();
        return (service, client);
    }

    public (ChatAgentService Service, FakeOpenRouterClient Client, SystemPromptService Prompt) BuildWithPromptHandle()
    {
        var (service, client, prompt) = BuildInternal(withPromptHandle: true);
        return (service, client, prompt!);
    }

    private (ChatAgentService Service, FakeOpenRouterClient Client, SystemPromptService? Prompt) BuildInternal(
        bool withPromptHandle = false)
    {
        var client = _client ?? new FakeOpenRouterClient(_deltas, _exception);
        var storage = _storageFixture.CreateStorage();
        var options = Options.Create(new OpenRouterOptions
        {
            BaseUrl = "https://test.local/",
            Model = "test-model",
            SystemPrompt = _optionsPrompt ?? OpenRouterOptions.DefaultSystemPrompt
        });
        var catalog = new ModelCatalogService(_httpFixture.CreateUnusedFactory());
        if (_catalogModels.Count > 0)
        {
            catalog.SeedForTest(_catalogModels);
        }
        else if (_seedDefaultCatalog)
        {
            catalog.SeedForTest([DefaultModel]);
        }
        else
        {
            catalog.SeedForTest([]);
        }

        var selection = new SelectedModelService(storage);
        selection.SetCurrentModelIdForTest(_selectedModelId);
        var systemPrompt = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);
        systemPrompt.SetCurrentPromptForTest(_uiPrompt);
        var chatSettings = TestSupport.NewChatSettings(storage);
        chatSettings.SetForTest(_reasoning, _temperature, _maxTokens);

        var service = new ChatAgentService(
            client,
            options,
            NullLogger<ChatAgentService>.Instance,
            selection,
            catalog,
            systemPrompt,
            chatSettings,
            _conversationWriter ?? NullActiveConversationWriter.Instance);

        return (service, client, withPromptHandle ? systemPrompt : null);
    }

    public static ChatAgentService BuildServiceWithClient(FakeOpenRouterClient client)
    {
        var storageFixture = new ProtectedBrowserStorageFixture();
        var httpFixture = new HttpMessageHandlerFixture();
        return new ChatAgentServiceBuilder(storageFixture, httpFixture)
            .WithClient(client)
            .Build()
            .Service;
    }

    private static OpenRouterModel DefaultModel => new(
        "test-model",
        "test-model",
        128_000L,
        DateTimeOffset.UtcNow,
        "text->text",
        new OpenRouterPricing(0.0000025m, 0.00001m),
        ["tools", "reasoning", "tool_choice"]);
}

/// <summary>
/// Static helpers shared by ChatAgentService tests.
/// </summary>
public static class ChatAgentServiceTestHelpers
{
    public static async Task Consume(IAsyncEnumerable<ChatDisplayMessage> stream)
    {
        await foreach (var _ in stream)
        {
            /* drain */
        }
    }

    public static async Task<List<ChatDisplayMessage>> ConsumeToList(IAsyncEnumerable<ChatDisplayMessage> stream)
    {
        var list = new List<ChatDisplayMessage>();
        await foreach (var m in stream) list.Add(m);
        return list;
    }

    public static JsonElement RequestMessages(FakeOpenRouterClient fake)
    {
        Assert.NotNull(fake.LastRequest);
        var json = JsonSerializer.Serialize(fake.LastRequest);
        return JsonDocument.Parse(json).RootElement.GetProperty("messages");
    }
}

/// <summary>
/// xUnit fixture that composes the storage and HTTP fixtures and exposes a
/// <see cref="ChatAgentServiceBuilder"/>.
/// </summary>
public sealed class ChatAgentServiceFixture
{
    public ProtectedBrowserStorageFixture Storage { get; } = new();
    public HttpMessageHandlerFixture Http { get; } = new();

    public ChatAgentServiceBuilder CreateBuilder() => new(Storage, Http);
}
