using System.Text.Json;
using Agentic.Chat.Models;

namespace Agentic.Chat.Tests;

public class ApiChatMessageContentTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void FromParts_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => ApiChatMessageContent.FromParts([]));
    }

    [Fact]
    public void GetDisplayText_Multipart_ReturnsTextPart()
    {
        var content = ApiChatMessageContent.FromParts(
        [
            new ApiContentPart("image_url", null, new ApiImageUrl("data:image/png;base64,x")),
            new ApiContentPart("text", "hello", null)
        ]);

        Assert.Equal("hello", content.GetDisplayText());
    }

    [Fact]
    public void JsonConverter_RoundTripsPlainText()
    {
        var original = ApiChatMessageContent.FromText("plain");

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<ApiChatMessageContent>(json, JsonOptions);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void JsonConverter_RoundTripsMultipart()
    {
        var original = ApiChatMessageContent.FromParts(
        [
            new ApiContentPart("text", "hi", null),
            new ApiContentPart("image_url", null, new ApiImageUrl("data:image/jpeg;base64,abc"))
        ]);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<ApiChatMessageContent>(json, JsonOptions);

        Assert.False(restored.IsText);
        Assert.Equal(2, restored.Parts.Count);
    }

    [Fact]
    public void JsonConverter_Read_InvalidToken_Throws()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ApiChatMessageContent>("42", JsonOptions));
    }

    [Fact]
    public void Equals_DefaultInstances_AreEqual()
    {
        ApiChatMessageContent left = default;
        ApiChatMessageContent right = default;

        Assert.True(left.Equals(right));
        Assert.Equal(0, left.GetHashCode());
    }

    [Fact]
    public void FromParts_SnapshotsInputList()
    {
        var parts = new List<ApiContentPart>
        {
            new("text", "hello", null)
        };
        var content = ApiChatMessageContent.FromParts(parts);
        parts.Add(new ApiContentPart("image_url", null, new ApiImageUrl("data:image/png;base64,x")));

        Assert.Single(content.Parts);
    }

    [Fact]
    public void Equals_NonDefaultToDefault_ReturnsFalse()
    {
        var left = ApiChatMessageContent.FromText("x");
        ApiChatMessageContent right = default;

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void GetHashCode_MultipartWithImage_IncludesImageUrl()
    {
        var content = ApiChatMessageContent.FromParts(
        [
            new ApiContentPart("text", "caption", null),
            new ApiContentPart("image_url", null, new ApiImageUrl("data:image/png;base64,x"))
        ]);

        Assert.NotEqual(0, content.GetHashCode());
    }

    [Fact]
    public void GetHashCode_TextOnlyMultipart_UsesPartFields()
    {
        var content = ApiChatMessageContent.FromParts(
            [new ApiContentPart("text", "only text", null)]);

        Assert.NotEqual(0, content.GetHashCode());
    }

    [Fact]
    public void Equals_TextContent_ComparesOrdinally()
    {
        var a = ApiChatMessageContent.FromText("same");
        var b = ApiChatMessageContent.FromText("same");
        var c = ApiChatMessageContent.FromText("other");

        Assert.True(a == b);
        Assert.False(a == c);
        Assert.True(a != c);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Text_OnMultipart_Throws()
    {
        var content = ApiChatMessageContent.FromParts(
            [new ApiContentPart("text", "x", null)]);

        Assert.Throws<InvalidOperationException>(() => content.Text);
    }

    [Fact]
    public void Parts_OnPlainText_Throws()
    {
        var content = ApiChatMessageContent.FromText("plain");

        Assert.Throws<InvalidOperationException>(() => content.Parts);
    }

    [Fact]
    public void GetDisplayText_MultipartWithoutText_ReturnsEmpty()
    {
        var content = ApiChatMessageContent.FromParts(
            [new ApiContentPart("image_url", null, new ApiImageUrl("data:image/png;base64,x"))]);

        Assert.Equal(string.Empty, content.GetDisplayText());
    }

    [Fact]
    public void Equals_Multipart_ComparesParts()
    {
        var left = ApiChatMessageContent.FromParts(
            [new ApiContentPart("text", "a", null)]);
        var right = ApiChatMessageContent.FromParts(
            [new ApiContentPart("text", "a", null)]);

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left.Equals(ApiChatMessageContent.FromText("b")));
    }

    [Fact]
    public void Equals_ObjectNull_ReturnsFalse()
    {
        var content = ApiChatMessageContent.FromText("x");
        Assert.False(content.Equals((object?)null));
    }

    [Fact]
    public void JsonConverter_Read_EmptyString_ReturnsEmptyText()
    {
        var restored = JsonSerializer.Deserialize<ApiChatMessageContent>("\"\"", JsonOptions);
        Assert.Equal(string.Empty, restored.GetDisplayText());
    }

    [Fact]
    public void JsonConverter_Read_EmptyArray_ThrowsFromEmptyParts()
    {
        Assert.Throws<ArgumentException>(() =>
            JsonSerializer.Deserialize<ApiChatMessageContent>("[]", JsonOptions));
    }
}
