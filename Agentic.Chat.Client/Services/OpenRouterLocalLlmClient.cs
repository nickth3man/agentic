using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Chat.Models;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Services;

/// <summary>
/// Browser-resident <see cref="ILocalLlmClient"/> adapter backed by the same
/// OpenRouter account the WASM client already uses for chat. Picked up a user
/// credential through <see cref="OpenRouterCredentialService"/>; falls back to
/// <c>UnavailableLocalLlm</c> semantics (throws on call) when the visitor has
/// not entered a key.
/// </summary>
public sealed class OpenRouterLocalLlmClient : ILocalLlmClient
{
    private const string CouncilModel = "openrouter/free";
    private const int CouncilMaxTokens = 320;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly OpenRouterCredentialService _credentials;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<OpenRouterLocalLlmClient> _logger;

    public OpenRouterLocalLlmClient(
        HttpClient http,
        OpenRouterCredentialService credentials,
        IOptions<OpenRouterOptions> options,
        ILogger<OpenRouterLocalLlmClient> logger)
    {
        _http = http;
        _credentials = credentials;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GenerateCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        await _credentials.InitializeAsync().ConfigureAwait(false);
        var apiKey = await _credentials.GetKeyForModelAsync(CouncilModel).ConfigureAwait(false);

        var request = new CouncilCompletionRequest(
            Model: CouncilModel,
            Messages: new[]
            {
                new ApiChatMessage("system", ApiChatMessageContent.FromText(systemPrompt), Reasoning: null),
                new ApiChatMessage("user", ApiChatMessageContent.FromText(userPrompt), Reasoning: null),
            },
            Stream: false,
            MaxTokens: CouncilMaxTokens);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (!string.IsNullOrWhiteSpace(_options.HttpReferer))
        {
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", _options.HttpReferer);
        }
        if (!string.IsNullOrWhiteSpace(_options.AppTitle))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-OpenRouter-Title", _options.AppTitle);
        }

        using var response = await _http
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new HttpRequestException(
                $"OpenRouter returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 200)}");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var envelope = await JsonSerializer
            .DeserializeAsync<CouncilCompletionResponse>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        var text = envelope?.FirstMessageText();
        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidOperationException("OpenRouter returned no assistant message.")
            : text.Trim();
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + "…";
    }

    private sealed record CouncilCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ApiChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed class CouncilCompletionResponse
    {
        [JsonPropertyName("choices")] public List<CouncilChoice>? Choices { get; init; }

        public string? FirstMessageText()
        {
            if (Choices is null || Choices.Count == 0) return null;
            var message = Choices[0].Message;
            return message is null ? null : message.Content.GetDisplayText();
        }
    }

    private sealed class CouncilChoice
    {
        [JsonPropertyName("message")] public CouncilMessage? Message { get; init; }
    }

    private sealed class CouncilMessage
    {
        [JsonPropertyName("content")] public ApiChatMessageContent Content { get; init; }
    }
}
