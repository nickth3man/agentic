using System.Text.Json;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Tests;

public class ChatAgentServiceResetTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    [Fact]
    public async Task Reset_AfterCompletedSend_ClearsDisplayMessages()
    {
        var (service, _) = CreateService();
        await Consume(service.SendStreamingAsync("hello"));
        Assert.Equal(2, service.Messages.Count);

        service.Reset();

        Assert.Empty(service.Messages);
    }

    [Fact]
    public async Task Reset_AfterCompletedSend_NextRequestHasOnlySystemAndUser()
    {
        var (service, fake) = CreateService();

        await Consume(service.SendStreamingAsync("first"));
        service.Reset();

        await Consume(service.SendStreamingAsync("second"));

        var messages = RequestMessages(fake);
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a helpful chat agent.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("second", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Reset_WhenEmpty_LeavesDisplayEmpty_AndNextSendIsSystemPlusUser()
    {
        var (service, fake) = CreateService();

        Assert.Empty(service.Messages);
        service.Reset();
        Assert.Empty(service.Messages);

        await Consume(service.SendStreamingAsync("hi"));

        var messages = RequestMessages(fake);
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("hi", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Reset_WhileStreaming_IsNoOp_ThenClearsAfterComplete()
    {
        var (service, _) = CreateService(new StreamDelta("Hello", null));

        await using var enumerator = service.SendStreamingAsync("hi").GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, service.Messages.Count);

        service.Reset();
        Assert.Equal(2, service.Messages.Count);

        while (await enumerator.MoveNextAsync())
        {
            /* drain */
        }

        Assert.Equal(2, service.Messages.Count);
        service.Reset();
        Assert.Empty(service.Messages);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (ChatAgentService Service, FakeOpenRouterClient Client) CreateService(
        params StreamDelta[] deltas)
    {
        var fake = new FakeOpenRouterClient(deltas);
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

    private static JsonElement RequestMessages(FakeOpenRouterClient fake)
    {
        Assert.NotNull(fake.LastRequest);
        var json = JsonSerializer.Serialize(
            fake.LastRequest,
            JsonOptions);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("messages").Clone();
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


