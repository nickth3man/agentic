using System.Text.Json;
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

    [Fact]
    public void UsageOnlyChunk_ReturnsDeltaWithUsage()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{}}],\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":340,\"total_cost\":0.0041}}");

        Assert.NotNull(delta);
        Assert.Null(delta!.Content);
        Assert.Null(delta.Reasoning);
        Assert.NotNull(delta.Usage);
        Assert.Equal(1200, delta.Usage!.PromptTokens);
        Assert.Equal(340, delta.Usage.CompletionTokens);
        Assert.Equal(0.0041m, delta.Usage.Cost);
    }

    [Fact]
    public void ContentAndUsage_BothPresent()
    {
        var delta = OpenRouterClient.DecodeDelta(
            "{\"choices\":[{\"delta\":{\"content\":\"done\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5}}");

        Assert.NotNull(delta);
        Assert.Equal("done", delta!.Content);
        Assert.Equal(10, delta.Usage!.PromptTokens);
        Assert.Equal(5, delta.Usage.CompletionTokens);
        Assert.Null(delta.Usage.Cost);
    }

    [Fact]
    public void ParseUsage_ReadsRootUsageElement()
    {
        using var doc = JsonDocument.Parse(
            "{\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":50,\"cost\":0.01}}");
        var usage = OpenRouterClient.ParseUsage(doc.RootElement);

        Assert.NotNull(usage);
        Assert.Equal(100, usage!.PromptTokens);
        Assert.Equal(50, usage.CompletionTokens);
        Assert.Equal(0.01m, usage.Cost);
    }

    [Fact]
    public void ParseUsage_UsageNotObject_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"usage\":[]}");
        Assert.Null(OpenRouterClient.ParseUsage(doc.RootElement));
    }

    [Fact]
    public void ParseUsage_MissingPromptTokens_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"usage\":{\"completion_tokens\":2}}");
        Assert.Null(OpenRouterClient.ParseUsage(doc.RootElement));
    }

    [Fact]
    public void ParseUsage_NonNumericPromptTokens_ReturnsNull()
    {
        using var doc = JsonDocument.Parse(
            "{\"usage\":{\"prompt_tokens\":\"nope\",\"completion_tokens\":2}}");
        Assert.Null(OpenRouterClient.ParseUsage(doc.RootElement));
    }

    [Fact]
    public void ParseUsage_MissingUsage_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"choices\":[]}");
        Assert.Null(OpenRouterClient.ParseUsage(doc.RootElement));
    }

    [Fact]
    public void ParseUsage_InvalidShape_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"usage\":{\"prompt_tokens\":\"nope\"}}");
        Assert.Null(OpenRouterClient.ParseUsage(doc.RootElement));
    }

    [Fact]
    public void ParseUsage_NonNumericCompletionTokens_ReturnsNull()
    {
        using var doc = JsonDocument.Parse(
            "{\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":\"nope\"}}");
        Assert.Null(OpenRouterClient.ParseUsage(doc.RootElement));
    }

    [Fact]
    public void ParseUsage_NonNumericCost_IsIgnored()
    {
        using var doc = JsonDocument.Parse(
            "{\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":2,\"total_cost\":\"nope\",\"cost\":\"nope\"}}");
        var usage = OpenRouterClient.ParseUsage(doc.RootElement);

        Assert.NotNull(usage);
        Assert.Null(usage!.Cost);
    }

    [Fact]
    public void ParseUsage_PrefersTotalCost_OverCost()
    {
        using var doc = JsonDocument.Parse(
            "{\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":2,\"total_cost\":0.5,\"cost\":0.1}}");
        var usage = OpenRouterClient.ParseUsage(doc.RootElement);

        Assert.Equal(0.5m, usage!.Cost);
    }
}
