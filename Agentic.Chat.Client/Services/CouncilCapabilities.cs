using System;
using System.Threading.Tasks;
using Agentic.Chat.Services;
using Agentic.Chat.Services.MultiAgent;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Client.Services;

/// <summary>
/// Pages / browser-resident <see cref="ICouncilCapabilities"/>: tracks the
/// visitor's own OpenRouter API key (see <see cref="OpenRouterCredentialService"/>)
/// and exposes a live <c>IsCouncilEnabled</c> flag plus a reason for the UI.
/// Subscribes once to <see cref="OpenRouterCredentialService.OnChange"/> and
/// unhooks on dispose.
/// </summary>
public sealed class CouncilCapabilities : ICouncilCapabilities, IDisposable
{
    private static readonly Action<ILogger, Exception?> LogInitFailed =
        LoggerMessage.Define(LogLevel.Warning, default, "Failed to initialize credentials for CouncilCapabilities.");

    private readonly OpenRouterCredentialService _credentials;
    private readonly ILogger<CouncilCapabilities> _logger;

    public CouncilCapabilities(
        OpenRouterCredentialService credentials,
        ILogger<CouncilCapabilities> logger)
    {
        _credentials = credentials;
        _logger = logger;
        _credentials.OnChange += OnCredentialChanged;
        _ = RefreshAsync();
    }

    public bool IsCouncilEnabled { get; private set; }
    public string DisabledReason { get; private set; } = "";

    public event Action? Changed;

    private async Task RefreshAsync()
    {
        try
        {
            await _credentials.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogInitFailed(_logger, ex);
        }

        IsCouncilEnabled = _credentials.HasUserKey;
        DisabledReason = _credentials.HasUserKey
            ? ""
            : "Multi-agent council needs your OpenRouter API key. Open the key settings (top-right of the chat header) to add one.";
        Changed?.Invoke();
    }

    private void OnCredentialChanged() => _ = RefreshAsync();

    public void Dispose() => _credentials.OnChange -= OnCredentialChanged;
}
