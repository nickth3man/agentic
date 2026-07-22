using Agentic.Chat.Models;

namespace Agentic.Chat.Tests;

public class OpenRouterModelTests
{
    private static OpenRouterModel Build(
        string id = "openai/gpt-4o",
        string name = "GPT-4o",
        long context = 128_000,
        long created = 1_700_000_000,
        string modality = "text->text",
        decimal promptPrice = 0.0000025m,
        decimal completionPrice = 0.00001m,
        params string[] supported)
        => new(
            Id: id,
            Name: name,
            ContextLength: context,
            Created: DateTimeOffset.FromUnixTimeSeconds(created),
            Modality: modality,
            Pricing: new OpenRouterPricing(promptPrice, completionPrice),
            SupportedParameters: supported);

    [Fact]
    public void Provider_SplitsOnFirstSlash()
    {
        var m = Build(id: "openai/gpt-4o");
        Assert.Equal("openai", m.Provider);
    }

    [Fact]
    public void Provider_ForCompoundSlug_ReturnsLeadingSegment()
    {
        var m = Build(id: "anthropic/claude-3.5-sonnet:beta");
        Assert.Equal("anthropic", m.Provider);
    }

    [Fact]
    public void IsFree_True_WhenIdSuffixIsFreeCaseInsensitive()
    {
        var m = Build(id: "meta-llama/llama-3.3-70b-instruct:Free",
            promptPrice: 0.0001m, completionPrice: 0.0002m);
        Assert.True(m.IsFree);
    }

    [Fact]
    public void IsFree_True_WhenBothPricesAreZero()
    {
        var m = Build(id: "somevendor/reasoning-pro",
            promptPrice: 0m, completionPrice: 0m);
        Assert.True(m.IsFree);
    }

    [Fact]
    public void IsFree_False_WhenPaidAndNonFreeSlug()
    {
        var m = Build(id: "openai/gpt-4o",
            promptPrice: 0.0000025m, completionPrice: 0.00001m);
        Assert.False(m.IsFree);
    }

    [Fact]
    public void PromptPerMillionTokens_MultipliesByMillion()
    {
        // 0.0000025 USD/token -> 2.5 USD/M tokens.
        var m = Build(promptPrice: 0.0000025m, completionPrice: 0m);
        Assert.Equal(2.5m, m.PromptPerMillionTokens);
    }

    [Fact]
    public void CompletionPerMillionTokens_MultipliesByMillion()
    {
        // 0.00001 USD/token -> 10 USD/M tokens.
        var m = Build(promptPrice: 0m, completionPrice: 0.00001m);
        Assert.Equal(10m, m.CompletionPerMillionTokens);
    }

    [Fact]
    public void SupportsReasoning_True_WhenReasoningListed()
    {
        var m = Build(supported: new[] { "tools", "reasoning", "tool_choice" });
        Assert.True(m.SupportsReasoning);
    }

    [Fact]
    public void SupportsReasoning_True_WhenReasoningListed_CaseInsensitive()
    {
        var m = Build(supported: new[] { "REASONING" });
        Assert.True(m.SupportsReasoning);
    }

    [Fact]
    public void SupportsReasoning_False_WhenReasoningAbsent()
    {
        var m = Build(supported: new[] { "tools", "tool_choice" });
        Assert.False(m.SupportsReasoning);
    }

    [Fact]
    public void SupportsReasoning_False_OnEmptySupportedParametersList()
    {
        var m = Build(supported: Array.Empty<string>());
        Assert.False(m.SupportsReasoning);
    }
}
