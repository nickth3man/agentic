using System.Text.Json;
using Agentic.Chat.Models;

namespace Agentic.Chat.Tests;

public class ApiChatMessageSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void UserWithImage_RejectsEmptyText()
    {
        Assert.Throws<ArgumentException>(() =>
            ApiChatMessage.UserWithImage("  ", "data:image/jpeg;base64,abc"));
    }

    [Fact]
    public void UserWithImage_RejectsEmptyImageUrl()
    {
        Assert.Throws<ArgumentException>(() =>
            ApiChatMessage.UserWithImage("hello", " "));
    }

    [Fact]
    public void UserWithImage_SerializesMultipartOpenAiShape()
    {
        var message = ApiChatMessage.UserWithImage(
            "What is this?",
            "data:image/jpeg;base64,abc123");

        var json = JsonSerializer.Serialize(message, JsonOptions);

        Assert.Contains("\"role\":\"user\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"text\"", json, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"What is this?\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"image_url\"", json, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"data:image/jpeg;base64,abc123\"", json, StringComparison.Ordinal);
        Assert.StartsWith("{\"role\":\"user\",\"content\":[", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainTextMessage_StillSerializesAsStringContent()
    {
        var message = new ApiChatMessage("system", "You are helpful.", null);

        var json = JsonSerializer.Serialize(message, JsonOptions);

        Assert.Equal("{\"role\":\"system\",\"content\":\"You are helpful.\"}", json);
    }
}
