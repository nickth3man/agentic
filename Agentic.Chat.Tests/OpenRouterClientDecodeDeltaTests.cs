using Agentic.Chat.Models;
using Agentic.Chat.Services;

namespace Agentic.Chat.Tests;

public class OpenRouterClientDecodeDeltaTests
{
    [Fact]
    public void InvalidJson_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta("not json");

        Assert.Null(delta);
    }

    [Fact]
    public void MissingChoices_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta("{\"foo\":\"bar\"}");

        Assert.Null(delta);
    }

    [Fact]
    public void EmptyChoicesArray_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta("{\"choices\":[]}");

        Assert.Null(delta);
    }

    [Fact]
    public void MissingDelta_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"finish_reason\":\"stop\"}]}");

        Assert.Null(delta);
    }

    [Fact]
    public void EmptyDelta_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{}}]}");

        Assert.Null(delta);
    }

    [Fact]
    public void ContentString_AppendsAndReturnsTrue()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}");

        Assert.Equal(new StreamDelta("Hello", null), delta);
    }

    [Fact]
    public void ContentString_AppendsAcrossMultipleCalls()
    {
        var first = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}");
        var second = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"content\":\", world\"}}]}");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Hello, world", first!.Content + second!.Content);
    }

    [Fact]
    public void EmptyContentString_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"content\":\"\"}}]}");

        Assert.Null(delta);
    }

    [Fact]
    public void NullContentToken_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"content\":null}}]}");

        Assert.Null(delta);
    }

    [Fact]
    public void NonStringContentToken_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"content\":42}}]}");

        Assert.Null(delta);
    }

    [Fact]
    public void StringReasoning_AppendsAndReturnsTrue()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"reasoning\":\"thinking...\"}}]}");

        Assert.Equal(new StreamDelta(null, "thinking..."), delta);
    }

    [Fact]
    public void StringReasoning_TakesPrecedenceOverReasoningDetails()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"reasoning\":\"top\",\"reasoning_details\":[{\"text\":\"bottom\"}]}}]}");

        Assert.NotNull(delta);
        Assert.Equal("top", delta!.Reasoning);
    }

    [Fact]
    public void ReasoningDetailsArray_AccumulatesTexts()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"reasoning_details\":[" +
                "{\"text\":\"a\"}," +
                "{\"text\":\"b\"}," +
                "{\"foo\":\"bar\"}," +
                "{\"text\":\"\"}" +
            "]}}]}");

        Assert.NotNull(delta);
        // Non-text entry and empty-text entry skipped; "a" + "b" accumulated.
        Assert.Equal("ab", delta!.Reasoning);
    }

    [Fact]
    public void ReasoningDetailsNotArray_ReturnsNull()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"reasoning_details\":\"oops\"}}]}");

        Assert.Null(delta);
    }

    [Fact]
    public void ReasoningAndContent_BothAccumulate()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"reasoning\":\"r\",\"content\":\"c\"}}]}");

        Assert.Equal(new StreamDelta("c", "r"), delta);
    }

    [Fact]
    public void ReasoningDetailsEntryMissingText_Skipped()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"reasoning_details\":[" +
                "{\"type\":\"summary\"}," +
                "{\"text\":\"ok\"}," +
                "{\"text\":\"\"}" +
            "]}}]}");

        Assert.NotNull(delta);
        Assert.Equal("ok", delta!.Reasoning);
    }
}
