using Agentic.Chat.Services;

namespace Agentic.Chat.Tests;

public class ChatAgentServiceTruncateTests
{
    [Fact]
    public void ShorterThanMax_ReturnedAsIs()
    {
        Assert.Equal("abc", ChatAgentService.Truncate("abc", 5));
    }

    [Fact]
    public void ExactlyMax_ReturnedAsIs()
    {
        Assert.Equal("abcde", ChatAgentService.Truncate("abcde", 5));
    }

    [Fact]
    public void LongerThanMax_TruncatedWithEllipsis()
    {
        Assert.Equal("abc\u2026", ChatAgentService.Truncate("abcdef", 3));
    }

    [Fact]
    public void EmptyString_ReturnedAsIs()
    {
        Assert.Equal(string.Empty, ChatAgentService.Truncate(string.Empty, 5));
    }
}
