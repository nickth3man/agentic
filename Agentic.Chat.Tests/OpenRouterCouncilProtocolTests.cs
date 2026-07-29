using System.Text;
using System.Text.Json;
using Agentic.Chat.Services.MultiAgent;

namespace Agentic.Chat.Tests;

public sealed class OpenRouterCouncilProtocolTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateRequest_UsesQwenWithoutReasoning()
    {
        var request = OpenRouterCouncilProtocol.CreateRequest("system", "user");
        var json = JsonSerializer.SerializeToElement(request, JsonOptions);

        Assert.Equal("qwen/qwen3.7-flash", json.GetProperty("model").GetString());
        Assert.Equal(320, json.GetProperty("max_tokens").GetInt32());
        Assert.False(json.GetProperty("stream").GetBoolean());
        Assert.Equal("none", json.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("system", json.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("system", json.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("user", json.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.Equal("user", json.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ReadResponseTextAsync_ReadsStringContent()
    {
        var text = await ReadAsync("""
            {"choices":[{"message":{"content":"answer"}}]}
            """);

        Assert.Equal("answer", text);
    }

    [Fact]
    public async Task ReadResponseTextAsync_ReadsMultipartContent()
    {
        var text = await ReadAsync("""
            {"choices":[{"message":{"content":[{"type":"text","text":"answer"}]}}]}
            """);

        Assert.Equal("answer", text);
    }

    [Theory]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}")]
    [InlineData("{\"choices\":[{\"message\":{}}]}")]
    public async Task ReadResponseTextAsync_ReturnsNullWithoutContent(string json)
    {
        Assert.Null(await ReadAsync(json));
    }

    private static async Task<string?> ReadAsync(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await OpenRouterCouncilProtocol.ReadResponseTextAsync(stream, JsonOptions);
    }
}
