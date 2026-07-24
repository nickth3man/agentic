using Agentic.Chat.Services;

namespace Agentic.Chat.Tests;

public class ConversationTitleTests
{
    [Fact]
    public void FromFirstUserMessage_TrimsAndReturnsShortText()
    {
        Assert.Equal("Hello world", ConversationTitle.FromFirstUserMessage("  Hello world  "));
    }

    [Fact]
    public void FromFirstUserMessage_EmptyOrNull_ReturnsDefault()
    {
        Assert.Equal(ConversationTitle.Default, ConversationTitle.FromFirstUserMessage("   "));
        Assert.Equal(ConversationTitle.Default, ConversationTitle.FromFirstUserMessage(null));
    }

    [Fact]
    public void FromFirstUserMessage_LongText_TruncatesToMaxWithEllipsis()
    {
        var input = new string('a', ConversationTitle.MaxLength + 10);
        var title = ConversationTitle.FromFirstUserMessage(input);

        Assert.Equal(ConversationTitle.MaxLength + 1, title.Length);
        Assert.Equal(new string('a', ConversationTitle.MaxLength) + "…", title);
    }

    [Fact]
    public void FromFirstUserMessage_CollapsesInternalWhitespace()
    {
        Assert.Equal("one two three", ConversationTitle.FromFirstUserMessage("one\n\ttwo   three"));
    }
}
