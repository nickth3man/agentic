using System.Text.Json.Serialization;

namespace Agentic.Chat.Models;

// Typed API-transcript message. Content is plain text for system/assistant turns and
// most user turns; vision user turns use multipart content (text + image_url parts).
public sealed record ApiChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] ApiChatMessageContent Content,
    [property: JsonPropertyName("reasoning")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Reasoning)
{
    [JsonIgnore]
    public string TextContent => Content.GetDisplayText();

    public static ApiChatMessage UserWithImage(string text, string imageDataUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageDataUrl);

        return new(
            "user",
            ApiChatMessageContent.FromParts(
            [
                new ApiContentPart(ApiChatMessageContent.TextPartType, text, null),
                new ApiContentPart(ApiChatMessageContent.ImageUrlPartType, null, new ApiImageUrl(imageDataUrl))
            ]),
            null);
    }
