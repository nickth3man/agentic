using System.Text.Json;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Agentic.Chat.Tests.Fixtures;

namespace Agentic.Chat.Tests;

public class ChatAgentServiceResetTests : IClassFixture<ChatAgentServiceFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ChatAgentServiceFixture _fixture;

    public ChatAgentServiceResetTests(ChatAgentServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Reset_AfterCompletedSend_ClearsDisplayMessages()
    {
        var (service, _) = _fixture.CreateBuilder().Build();
        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hello"));
        Assert.Equal(2, service.Messages.Count);

        service.Reset();

        Assert.Empty(service.Messages);
    }

    [Fact]
    public async Task Reset_AfterCompletedSend_NextRequestHasOnlySystemAndUser()
    {
        var (service, fake) = _fixture.CreateBuilder().Build();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("first"));
        service.Reset();

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("second"));

        var messages = ChatAgentServiceTestHelpers.RequestMessages(fake);
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a helpful chat agent.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("second", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Reset_WhenEmpty_LeavesDisplayEmpty_AndNextSendIsSystemPlusUser()
    {
        var (service, fake) = _fixture.CreateBuilder().Build();

        Assert.Empty(service.Messages);
        service.Reset();
        Assert.Empty(service.Messages);

        await ChatAgentServiceTestHelpers.Consume(service.SendStreamingAsync("hi"));

        var messages = ChatAgentServiceTestHelpers.RequestMessages(fake);
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("hi", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Reset_WhileStreaming_IsNoOp_ThenClearsAfterComplete()
    {
        var (service, _) = _fixture.CreateBuilder().WithDeltas(new StreamDelta("Hello", null)).Build();

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
}
