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
        Assert.False(message.ThinkingUserTouched);
        Assert.False(message.IsThinkingOpen);
    }

    [Fact]
    public void ThinkingPanel_OpensAutomaticallyWhileOnlyReasoningStreams()
    {
        var message = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };

        Assert.True(message.IsThinkingOpen);

        message.ApplyDelta(
            new StreamDelta(null, "Working through the problem."),
            DateTimeOffset.UnixEpoch);

        Assert.True(message.IsThinkingOpen);
    }

    [Fact]
    public void ThinkingPanel_CollapsesWhenAnswerStarts_UnlessUserToggledIt()
    {
        var answerAt = DateTimeOffset.UnixEpoch.AddSeconds(3);
        var automatic = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };
        var userOpened = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };
        var userClosed = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };

        userOpened.SetThinkingOpenByUser(true);
        userClosed.SetThinkingOpenByUser(false);

        automatic.ApplyDelta(new StreamDelta("Answer", null), answerAt);
        userOpened.ApplyDelta(new StreamDelta("Answer", null), answerAt);
        userClosed.ApplyDelta(new StreamDelta("Answer", null), answerAt);

        Assert.False(automatic.IsThinkingOpen);
        Assert.True(userOpened.IsThinkingOpen);
        Assert.False(userClosed.IsThinkingOpen);
        Assert.True(userOpened.ThinkingUserTouched);
        Assert.True(userClosed.ThinkingUserTouched);
    }

    [Fact]
    public void ThoughtDuration_UsesFirstContentTokenAndIsAvailableAfterCompletion()
    {
        var reasoningAt = DateTimeOffset.UnixEpoch;
        var contentAt = reasoningAt.AddSeconds(3);
        var message = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };

        message.ApplyDelta(new StreamDelta(null, "Reasoning"), reasoningAt);
        message.ApplyDelta(new StreamDelta("Answer", null), contentAt);
        message.MarkCompleted(contentAt.AddSeconds(1));

        Assert.Equal(reasoningAt, message.ReasoningStartedAt);
        Assert.Equal(contentAt, message.ContentStartedAt);
        Assert.Equal(3, message.ThoughtDurationSeconds);
    }

    [Fact]
    public void ReasoningOnlyMessage_UsesCompletionTimeForThoughtDuration()
    {
        var reasoningAt = DateTimeOffset.UnixEpoch;
        var message = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };

        message.ApplyDelta(new StreamDelta(null, "Reasoning"), reasoningAt);
        message.MarkCompleted(reasoningAt.AddSeconds(2));

        Assert.Null(message.ContentStartedAt);
        Assert.Equal(2, message.ThoughtDurationSeconds);
    }

    [Fact]
    public void ThoughtDuration_IsAbsentUntilReasoningAndAnEndTimeExist()
    {
        var message = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };

        Assert.Null(message.ThoughtDurationSeconds);

        message.ApplyDelta(new StreamDelta(null, "Reasoning"), DateTimeOffset.UnixEpoch);

        Assert.Null(message.ThoughtDurationSeconds);
    }

    [Fact]
    public void MarkCompleted_PreservesTheFirstCompletionTime()
    {
        var firstCompletion = DateTimeOffset.UnixEpoch.AddSeconds(2);
        var message = new ChatDisplayMessage { Role = "assistant", IsStreaming = true };

        message.MarkCompleted(firstCompletion);
        message.MarkCompleted(firstCompletion.AddSeconds(1));

        Assert.False(message.IsStreaming);
        Assert.Equal(firstCompletion, message.CompletedAt);
    }
}
