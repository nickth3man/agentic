using System.Text.Json.Serialization;

namespace Agentic.Chat.Models;

// Pricing comes from OpenRouter as two string-encoded decimals (USD per single token).
// The numeric form is preserved here so the catalog can multiply to a per-million figure
// without losing precision in the unit conversion. [JsonPropertyName] is attached to each
// record positional parameter so a future direct STJ round-trip would still bind the
// camelCase JSON keys ("prompt"/"completion") to the matching C# property names.
public sealed record OpenRouterPricing(
    [property: JsonPropertyName("prompt")] decimal PromptPerToken,
    [property: JsonPropertyName("completion")] decimal CompletionPerToken);

// Per-model record returned by GET /models. The constructor parameter types match the
// designer-bound surface (DateTimeOffset Created, flat string Modality). Raw JSON keys
// are tagged on each positional parameter so a direct STJ round-trip would still map
// camelCase JSON to the auto-generated properties; however the catalog's parse path
// instead routes through an internal DTO because Created's epoch-seconds source and
// Modality's nested source aren't directly STJ-deserializable into the public shape.
public sealed record OpenRouterModel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("context_length")] long ContextLength,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonPropertyName("modality")] string Modality,
    [property: JsonPropertyName("pricing")] OpenRouterPricing Pricing,
    [property: JsonPropertyName("supported_parameters")] IReadOnlyList<string> SupportedParameters)
{
    public string Provider => Id.Split('/')[0];

    public bool IsFree =>
        Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase)
        || (Pricing.PromptPerToken == 0m && Pricing.CompletionPerToken == 0m);

    public decimal PromptPerMillionTokens => Pricing.PromptPerToken * 1_000_000m;
    public decimal CompletionPerMillionTokens => Pricing.CompletionPerToken * 1_000_000m;

    public bool SupportsReasoning =>
        SupportedParameters.Contains("reasoning", StringComparer.OrdinalIgnoreCase);
}
