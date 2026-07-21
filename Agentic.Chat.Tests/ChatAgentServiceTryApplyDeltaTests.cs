using Agentic.Chat.Models;
using Agentic.Chat.Services;

namespace Agentic.Chat.Tests;

public class ChatAgentServiceTryApplyDeltaTests
{
    private static ChatDisplayMessage NewAssistant() => new() { Role = "assistant" };

    [Fact]
    public void InvalidJson_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta("not json", assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Content);
        Assert.Equal(string.Empty, assistant.Reasoning);
    }

    [Fact]
    public void MissingChoices_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta("{\"foo\":\"bar\"}", assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Content);
        Assert.Equal(string.Empty, assistant.Reasoning);
    }

    [Fact]
    public void EmptyChoicesArray_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta("{\"choices\":[]}", assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Content);
        Assert.Equal(string.Empty, assistant.Reasoning);
    }

    [Fact]
    public void MissingDelta_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"finish_reason\":\"stop\"}]}",
            assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Content);
        Assert.Equal(string.Empty, assistant.Reasoning);
    }

    [Fact]
    public void EmptyDelta_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{}}]}",
            assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Content);
        Assert.Equal(string.Empty, assistant.Reasoning);
    }

    [Fact]
    public void ContentString_AppendsAndReturnsTrue()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}",
            assistant);

        Assert.True(changed);
        Assert.Equal("Hello", assistant.Content);
        Assert.Equal(string.Empty, assistant.Reasoning);
    }

    [Fact]
    public void ContentString_AppendsAcrossMultipleCalls()
    {
        var assistant = NewAssistant();

        var first = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}",
            assistant);
        var second = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"content\":\", world\"}}]}",
            assistant);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal("Hello, world", assistant.Content);
    }

    [Fact]
    public void EmptyContentString_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"content\":\"\"}}]}",
            assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Content);
    }

    [Fact]
    public void NullContentToken_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"content\":null}}]}",
            assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Content);
    }

    [Fact]
    public void NonStringContentToken_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"content\":42}}]}",
            assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Content);
    }

    [Fact]
    public void StringReasoning_AppendsAndReturnsTrue()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"reasoning\":\"thinking...\"}}]}",
            assistant);

        Assert.True(changed);
        Assert.Equal("thinking...", assistant.Reasoning);
        Assert.Equal(string.Empty, assistant.Content);
    }

    [Fact]
    public void StringReasoning_TakesPrecedenceOverReasoningDetails()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"reasoning\":\"top\",\"reasoning_details\":[{\"text\":\"bottom\"}]}}]}",
            assistant);

        Assert.True(changed);
        // The `if (reasoning)` branch wins over the `else if (reasoning_details)` branch.
        Assert.Equal("top", assistant.Reasoning);
    }

    [Fact]
    public void ReasoningDetailsArray_AccumulatesTexts()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"reasoning_details\":[" +
                "{\"text\":\"a\"}," +
                "{\"text\":\"b\"}," +
                "{\"foo\":\"bar\"}," +
                "{\"text\":\"\"}" +
            "]}}]}",
            assistant);

        Assert.True(changed);
        // Non-text entry and empty-text entry skipped; "a" + "b" accumulated.
        Assert.Equal("ab", assistant.Reasoning);
    }

    [Fact]
    public void ReasoningDetailsNotArray_ReturnsFalse()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"reasoning_details\":\"oops\"}}]}",
            assistant);

        Assert.False(changed);
        Assert.Equal(string.Empty, assistant.Reasoning);
    }

    [Fact]
    public void ReasoningAndContent_BothAccumulate()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"reasoning\":\"r\",\"content\":\"c\"}}]}",
            assistant);

        Assert.True(changed);
        Assert.Equal("r", assistant.Reasoning);
        Assert.Equal("c", assistant.Content);
    }

    [Fact]
    public void ContentOnly_LeavesReasoningUnchanged()
    {
        var assistant = new ChatDisplayMessage { Role = "assistant", Reasoning = "prior" };

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"content\":\"x\"}}]}",
            assistant);

        Assert.True(changed);
        Assert.Equal("prior", assistant.Reasoning);
        Assert.Equal("x", assistant.Content);
    }

    [Fact]
    public void ReasoningDetailsEntryMissingText_Skipped()
    {
        var assistant = NewAssistant();

        var changed = ChatAgentService.TryApplyDelta(
            "{\"choices\":[{\"delta\":{\"reasoning_details\":[" +
                "{\"type\":\"summary\"}," +
                "{\"text\":\"ok\"}," +
                "{\"text\":\"\"}" +
            "]}}]}",
            assistant);

        Assert.True(changed);
        Assert.Equal("ok", assistant.Reasoning);
    }
}
