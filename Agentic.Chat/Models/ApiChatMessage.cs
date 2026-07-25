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
        => new(
            "user",
            ApiChatMessageContent.FromParts(
            [
                new ApiContentPart("text", text, null),
                new ApiContentPart("image_url", null, new ApiImageUrl(imageDataUrl))
            ]),
            null);
}
