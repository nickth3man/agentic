using Agentic.Chat.Models;

namespace Agentic.Chat.Tests;

public class ChatDisplayMessageTests
{
    [Fact]
    public void NewMessage_HasSensibleDefaults()
    {
        var message = new ChatDisplayMessage { Role = "user" };

        Assert.Equal("user", message.Role);
        Assert.Equal(string.Empty, message.Content);
        Assert.Equal(string.Empty, message.Reasoning);
        Assert.False(message.IsStreaming);
    }
}
