using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agentic.Chat.Models;

[JsonConverter(typeof(ApiChatMessageContentJsonConverter))]
public readonly struct ApiChatMessageContent : IEquatable<ApiChatMessageContent>
{
    internal const string TextPartType = "text";
    internal const string ImageUrlPartType = "image_url";

    private readonly object? _value;

    private ApiChatMessageContent(object value) => _value = value;

    private bool IsDefault => _value is null;

    public bool IsText => _value is string;

    public string Text => _value as string
        ?? throw new InvalidOperationException("Content is multipart, not plain text.");

    public IReadOnlyList<ApiContentPart> Parts => _value as IReadOnlyList<ApiContentPart>
        ?? throw new InvalidOperationException("Content is plain text, not multipart.");

    public static ApiChatMessageContent FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ApiChatMessageContent(text);
    }

    public static ApiChatMessageContent FromParts(IReadOnlyList<ApiContentPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("Multipart content requires at least one part.", nameof(parts));
        }

        return new ApiChatMessageContent(parts.ToList());
    }

    public static implicit operator ApiChatMessageContent(string text) => FromText(text);

    public string GetDisplayText()
    {
        if (IsText)
        {
            return Text;
        }

        return Parts.FirstOrDefault(p => p.Type == TextPartType)?.Text ?? string.Empty;
    }

    public bool Equals(ApiChatMessageContent other)
    {
        if (IsDefault)
        {
            return other.IsDefault;
        }

        if (other.IsDefault)
        {
            return false;
        }

        return IsText == other.IsText
            && (IsText
                ? string.Equals(Text, other.Text, StringComparison.Ordinal)
                : Parts.SequenceEqual(other.Parts));
    }

    public override bool Equals(object? obj) => obj is ApiChatMessageContent other && Equals(other);

    public override int GetHashCode()
    {
        if (_value is null)
        {
            return 0;
        }

        if (IsText)
        {
            return Text.GetHashCode(StringComparison.Ordinal);
        }

        var hash = new HashCode();
        foreach (var part in Parts)
        {
            hash.Add(part.Type, StringComparer.Ordinal);
            hash.Add(part.Text, StringComparer.Ordinal);
            hash.Add(part.ImageUrl?.Url, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ApiChatMessageContent left, ApiChatMessageContent right) => left.Equals(right);

    public static bool operator !=(ApiChatMessageContent left, ApiChatMessageContent right) => !left.Equals(right);
}

internal sealed class ApiChatMessageContentJsonConverter : JsonConverter<ApiChatMessageContent>
{
    public override ApiChatMessageContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ApiChatMessageContent.FromText(reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected string or array for message content.");
        }

        var parts = JsonSerializer.Deserialize<List<ApiContentPart>>(ref reader, options)!;
        return ApiChatMessageContent.FromParts(parts);
    }

    public override void Write(Utf8JsonWriter writer, ApiChatMessageContent value, JsonSerializerOptions options)
    {
        if (value.IsText)
        {
            writer.WriteStringValue(value.Text);
            return;
        }

        writer.WriteStartArray();
        foreach (var part in value.Parts)
        {
            writer.WriteStartObject();
            writer.WriteString("type", part.Type);
            if (part.Text is not null)
            {
                writer.WriteString("text", part.Text);
            }

            if (part.ImageUrl is not null)
            {
                writer.WriteStartObject("image_url");
                writer.WriteString("url", part.ImageUrl.Url);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
