using Agentic.Chat.Models;
using Agentic.Chat.Services;

namespace Agentic.Chat.Tests;

public class UsageFormatterTests
{
    [Fact]
    public void FormatMessageFooter_ShowsFree_ForFreeUsage()
    {
        var text = UsageFormatter.FormatMessageFooter(
            new MessageUsage(100, 50, 0m, IsFree: true));

        Assert.Contains("free", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$0.0000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatMessageFooter_FormatsTokensAndCost()
    {
        var text = UsageFormatter.FormatMessageFooter(
            new MessageUsage(1200, 340, 0.0041m));

        Assert.Contains("1.2k in", text, StringComparison.Ordinal);
        Assert.Contains("340 out", text, StringComparison.Ordinal);
        Assert.Contains("$0.0041", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatConversationTotal_SumsMessageCosts()
    {
        var messages = new List<ChatDisplayMessage>
        {
            new() { Role = "assistant", Usage = new MessageUsage(10, 5, 0.01m) },
            new() { Role = "assistant", Usage = new MessageUsage(20, 10, 0.02m) }
        };

        var total = UsageFormatter.FormatConversationTotal(messages);

        Assert.Contains("$0.03", total, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatConversationTotal_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, UsageFormatter.FormatConversationTotal([]));
    }

    [Fact]
    public void FormatConversationTotal_AllFree_ReturnsSessionFree()
    {
        var messages = new List<ChatDisplayMessage>
        {
            new() { Role = "assistant", Usage = new MessageUsage(10, 5, 0m, IsFree: true) }
        };

        Assert.Equal("Session · free", UsageFormatter.FormatConversationTotal(messages));
    }

    [Fact]
    public void FormatConversationTotal_UnknownCost_ReturnsSessionDash()
    {
        var messages = new List<ChatDisplayMessage>
        {
            new() { Role = "assistant", Usage = new MessageUsage(10, 5, null) }
        };

        Assert.Equal("Session · —", UsageFormatter.FormatConversationTotal(messages));
    }

    [Fact]
    public void FormatConversationTotal_ZeroKnownCostNonFree_ReturnsSessionFree()
    {
        var messages = new List<ChatDisplayMessage>
        {
            new() { Role = "assistant", Usage = new MessageUsage(10, 5, 0m) },
            new() { Role = "assistant", Usage = new MessageUsage(20, 10, 0m) }
        };

        Assert.Equal("Session · free", UsageFormatter.FormatConversationTotal(messages));
    }

    [Fact]
    public void MessageUsage_FromStored_ReturnsNull_WhenIncomplete()
    {
        Assert.Null(MessageUsage.FromStored(null, 5, 0.01m, false));
        Assert.Null(MessageUsage.FromStored(10, null, 0.01m, false));
    }

    [Fact]
    public void MessageUsage_FromStored_RoundTripsStoredValues()
    {
        var usage = MessageUsage.FromStored(1200, 340, 0.0041m, false);

        Assert.NotNull(usage);
        Assert.Equal(1200, usage!.PromptTokens);
        Assert.Equal(340, usage.CompletionTokens);
        Assert.Equal(0.0041m, usage.Cost);
    }

    [Fact]
    public void FormatConversationTotal_MixedFreeAndPaid_SumsPaidOnly()
    {
        var messages = new List<ChatDisplayMessage>
        {
            new() { Role = "assistant", Usage = new MessageUsage(10, 5, 0m, IsFree: true) },
            new() { Role = "assistant", Usage = new MessageUsage(20, 10, 0.02m) }
        };

        var total = UsageFormatter.FormatConversationTotal(messages);

        Assert.Contains("$0.02", total, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatConversationTotal_ZeroTotalNonFree_ReturnsSessionDash()
    {
        var messages = new List<ChatDisplayMessage>
        {
            new() { Role = "assistant", Usage = new MessageUsage(10, 5, null) }
        };

        Assert.Equal("Session · —", UsageFormatter.FormatConversationTotal(messages));
    }

    [Fact]
    public void FormatMessageFooter_WithoutCost_ReturnsTokensOnly()
    {
        var text = UsageFormatter.FormatMessageFooter(new MessageUsage(500, 20, null));

        Assert.Equal("500 in · 20 out", text);
    }

    [Fact]
    public void FormatTokenCount_LargeValues_UseCompactK()
    {
        Assert.Equal("10k", UsageFormatter.FormatTokenCount(10_000));
        Assert.Equal("12.5k", UsageFormatter.FormatTokenCount(12_500));
        Assert.Equal("999", UsageFormatter.FormatTokenCount(999));
    }

    [Fact]
    public void FormatConversationTotal_IgnoresNonAssistantMessages()
    {
        var messages = new List<ChatDisplayMessage>
        {
            new() { Role = "user", Usage = new MessageUsage(999, 999, 99m) },
            new() { Role = "assistant", Usage = new MessageUsage(10, 5, 0.01m) }
        };

        var total = UsageFormatter.FormatConversationTotal(messages);

        Assert.Contains("$0.01", total, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatConversationTotal_SkipsAssistantWithoutUsage()
    {
        var messages = new List<ChatDisplayMessage>
        {
            new() { Role = "assistant" },
            new() { Role = "assistant", Usage = new MessageUsage(10, 5, 0.01m) }
        };

        var total = UsageFormatter.FormatConversationTotal(messages);

        Assert.Contains("$0.01", total, StringComparison.Ordinal);
    }
}
