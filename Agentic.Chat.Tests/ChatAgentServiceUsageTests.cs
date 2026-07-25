using Agentic.Chat.Models;
using Agentic.Chat.Services;

namespace Agentic.Chat.Tests;

public class ChatAgentServiceUsageTests
{
    [Fact]
    public void FinalizeUsage_NoOp_WhenUsageNull()
    {
        var message = new ChatDisplayMessage { Role = "assistant" };

        ChatAgentService.FinalizeUsage(message, null);

        Assert.Null(message.Usage);
    }

    [Fact]
    public void FinalizeUsage_KeepsProvidedCost()
    {
        var message = new ChatDisplayMessage
        {
            Role = "assistant",
            Usage = new MessageUsage(10, 5, 0.01m)
        };
        var model = FreeModel();

        ChatAgentService.FinalizeUsage(message, model);

        Assert.Equal(0.01m, message.Usage!.Cost);
    }

    [Fact]
    public void FinalizeUsage_MarksFree_WhenZeroCostFreeModel()
    {
        var message = new ChatDisplayMessage
        {
            Role = "assistant",
            Usage = new MessageUsage(10, 5, null)
        };

        ChatAgentService.FinalizeUsage(message, FreeModel());

        Assert.True(message.Usage!.IsFree);
        Assert.Equal(0m, message.Usage.Cost);
    }

    [Fact]
    public void FinalizeUsage_DoesNotMarkFree_WhenCostNonZero()
    {
        var message = new ChatDisplayMessage
        {
            Role = "assistant",
            Usage = new MessageUsage(10, 5, 0.01m)
        };

        ChatAgentService.FinalizeUsage(message, FreeModel());

        Assert.False(message.Usage!.IsFree);
    }

    [Fact]
    public void FinalizeUsage_DoesNotMarkFree_WhenModelIsPaidTier()
    {
        var message = new ChatDisplayMessage
        {
            Role = "assistant",
            Usage = new MessageUsage(0, 0, 0m)
        };
        var paid = new OpenRouterModel(
            "provider/paid",
            "Paid",
            128_000L,
            DateTimeOffset.UtcNow,
            "text->text",
            new OpenRouterPricing(0.0000025m, 0.00001m),
            ["tools"]);

        ChatAgentService.FinalizeUsage(message, paid);

        Assert.False(message.Usage!.IsFree);
    }

    [Fact]
    public void FinalizeUsage_MarksFree_WhenExplicitZeroCostOnFreeModel()
    {
        var message = new ChatDisplayMessage
        {
            Role = "assistant",
            Usage = new MessageUsage(10, 5, 0m)
        };

        ChatAgentService.FinalizeUsage(message, FreeModel());

        Assert.True(message.Usage!.IsFree);
    }

    [Fact]
    public void FinalizeUsage_EstimatesCostFromPaidModelPricing()
    {
        var message = new ChatDisplayMessage
        {
            Role = "assistant",
            Usage = new MessageUsage(1000, 500, null)
        };
        var paid = new OpenRouterModel(
            "provider/paid",
            "Paid",
            128_000L,
            DateTimeOffset.UtcNow,
            "text->text",
            new OpenRouterPricing(0.0000025m, 0.00001m),
            ["tools"]);

        ChatAgentService.FinalizeUsage(message, paid);

        Assert.Equal(0.0075m, message.Usage!.Cost);
        Assert.False(message.Usage.IsFree);
    }

    [Fact]
    public void FinalizeUsage_LeavesCostNull_WhenNoModelInfo()
    {
        var message = new ChatDisplayMessage
        {
            Role = "assistant",
            Usage = new MessageUsage(10, 5, null)
        };

        ChatAgentService.FinalizeUsage(message, null);

        Assert.Null(message.Usage!.Cost);
        Assert.False(message.Usage.IsFree);
    }

    private static OpenRouterModel FreeModel()
        => new(
            "provider/model:free",
            "Free Model",
            128_000L,
            DateTimeOffset.UtcNow,
            "text->text",
            new OpenRouterPricing(0m, 0m),
            ["tools"]);
}
