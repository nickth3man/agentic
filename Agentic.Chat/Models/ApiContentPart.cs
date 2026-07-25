using System.Text.Json.Serialization;

namespace Agentic.Chat.Models;

public sealed record ApiImageUrl([property: JsonPropertyName("url")] string Url);

public sealed record ApiContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Text,
    [property: JsonPropertyName("image_url")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ApiImageUrl? ImageUrl);
