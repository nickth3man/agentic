using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Services.MultiAgent;

public sealed class OllamaLocalLlmClient : ILocalLlmClient
{
    private static readonly Action<ILogger, string, Exception?> LogOllamaUnavailable =
        LoggerMessage.Define<string>(LogLevel.Warning, default, "Local Ollama LLM endpoint at {BaseUrl} unavailable. Using fallback response generator.");

    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaLocalLlmClient> _logger;
    private readonly string _baseUrl;
    private readonly string _model;

    public OllamaLocalLlmClient(HttpClient httpClient, ILogger<OllamaLocalLlmClient> logger, string baseUrl = "http://localhost:11434", string model = "llama3.3")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    public async Task<string> GenerateCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var req = new OllamaGenerateRequest
            {
                Model = _model,
                System = systemPrompt,
                Prompt = userPrompt,
                Stream = false
            };

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/generate", req, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var res = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(res?.Response))
                {
                    return res.Response;
                }
            }
        }
        catch (Exception ex)
        {
            LogOllamaUnavailable(_logger, _baseUrl, ex);
        }
        // Return explicit unavailable message when local LLM endpoint is offline
        return "[Local LLM unavailable]";
    }

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("system")] public string System { get; set; } = string.Empty;
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")] public string Response { get; set; } = string.Empty;
    }
}
