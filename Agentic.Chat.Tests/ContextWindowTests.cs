using Agentic.Chat.Models;
using Agentic.Chat.Services;

namespace Agentic.Chat.Tests;

public class ContextWindowTests
{
    [Fact]
    public void EstimateTokens_UsesFourCharacterHeuristic()
    {
        var tokens = ContextWindow.EstimateTokens(
        [
            new ApiChatMessage("system", "abcde", null),
            new ApiChatMessage("assistant", string.Empty, "not counted")
        ]);

        Assert.Equal(2, tokens);
    }

    [Fact]
    public void TrimToBudget_KeepsSystemAndRecentMessages_RemovesOldestPair()
    {
        var result = ContextWindow.TrimToBudget(
        [
            new ApiChatMessage("system", "rule", null),
            new ApiChatMessage("user", "1111111111111111", null),
            new ApiChatMessage("assistant", "2222222222222222", "private reasoning"),
            new ApiChatMessage("user", "3333333333333333", null),
            new ApiChatMessage("assistant", "4444444444444444", null),
            new ApiChatMessage("user", "5555555555555555", null)
        ],
        contextLength: 20);

        Assert.Equal(21, result.TranscriptTokens);
        Assert.Equal(2, result.ExcludedMessageCount);
        Assert.Collection(
            result.Messages,
            message => Assert.Equal(("system", "rule"), (message.Role, message.Content)),
            message => Assert.Equal(("user", "3333333333333333"), (message.Role, message.Content)),
            message => Assert.Equal(("assistant", "4444444444444444"), (message.Role, message.Content)),
            message => Assert.Equal(("user", "5555555555555555"), (message.Role, message.Content)));
        Assert.DoesNotContain(result.Messages, message => message.Reasoning is not null);
    }

    [Fact]
    public void TrimToBudget_WithoutContextLimit_PreservesMessagesAndStripsReasoning()
    {
        var result = ContextWindow.TrimToBudget(
        [
            new ApiChatMessage("system", "rule", null),
            new ApiChatMessage("assistant", "answer", "private reasoning")
        ],
        contextLength: 0);

        Assert.Equal(0, result.ExcludedMessageCount);
        Assert.Equal(2, result.Messages.Count);
        Assert.Null(result.Messages[1].Reasoning);
    }

    [Fact]
    public void TrimToBudget_MalformedLeadingAssistant_IsRemovedWithoutDroppingFollowingMessage()
    {
        var result = ContextWindow.TrimToBudget(
        [
            new ApiChatMessage("system", "rule", null),
            new ApiChatMessage("assistant", "1111111111111111", null),
            new ApiChatMessage("user", "2222222222222222", null),
            new ApiChatMessage("assistant", "3333333333333333", null)
        ],
        contextLength: 15);

        Assert.Equal(1, result.ExcludedMessageCount);
        Assert.DoesNotContain(result.Messages, message => message.Content == "1111111111111111");
        Assert.Equal(3, result.Messages.Count);
    }

    [Fact]
    public void TrimToBudget_PreservesMinimumRecentMessages_WhenTheyExceedBudget()
    {
        var result = ContextWindow.TrimToBudget(
        [
            new ApiChatMessage("system", "rule", null),
            new ApiChatMessage("user", "1111111111111111", null),
            new ApiChatMessage("assistant", "2222222222222222", null)
        ],
        contextLength: 5);

        Assert.Equal(0, result.ExcludedMessageCount);
        Assert.Equal(3, result.Messages.Count);
    }

    [Fact]
    public void TrimToBudget_ConsecutiveUsers_AreTrimmedIndividually()
    {
        var result = ContextWindow.TrimToBudget(
        [
            new ApiChatMessage("system", "rule", null),
            new ApiChatMessage("user", "1111111111111111", null),
            new ApiChatMessage("user", "2222222222222222", null),
            new ApiChatMessage("assistant", "3333333333333333", null),
            new ApiChatMessage("user", "4444444444444444", null)
        ],
        contextLength: 12);

        Assert.Equal(3, result.ExcludedMessageCount);
        Assert.Equal("4444444444444444", result.Messages[1].Content);
    }

    [Fact]
    public void TrimToBudget_LeavesSystemMessagesInTheMiddleOfTranscript()
    {
        var result = ContextWindow.TrimToBudget(
        [
            new ApiChatMessage("system", "rule", null),
            new ApiChatMessage("user", "1111111111111111", null),
            new ApiChatMessage("system", "later rule", null),
            new ApiChatMessage("assistant", "2222222222222222", null),
            new ApiChatMessage("user", "3333333333333333", null)
        ],
        contextLength: 15);

        Assert.Contains(result.Messages, message => message.Content == "later rule");
        Assert.Equal(2, result.ExcludedMessageCount);
    }
}
