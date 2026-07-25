namespace Agentic.Chat.Models;

// Pricing comes from OpenRouter as two string-encoded decimals (USD per single token).
// The numeric form is preserved here so the catalog can multiply to a per-million figure
// without losing precision in the unit conversion.
public sealed record OpenRouterPricing(
    decimal PromptPerToken,
    decimal CompletionPerToken);

// Per-model record returned by GET /models. The constructor parameter types match the
// designer-bound surface (DateTimeOffset Created, flat string Modality). The catalog's
// parse path routes through an internal DTO because Created's epoch-seconds source and
// Modality's nested source aren't directly STJ-deserializable into the public shape.
public sealed record OpenRouterModel(
    string Id,
    string Name,
    long ContextLength,
    DateTimeOffset Created,
    string Modality,
    OpenRouterPricing Pricing,
    IReadOnlyList<string> SupportedParameters)
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
