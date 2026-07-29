using System;
using Microsoft.Extensions.Options;

namespace Agentic.Chat.Services.MultiAgent;

/// <summary>
/// Blazor Server implementation of <see cref="ICouncilCapabilities"/>. Reports
/// enabled iff the deployment was configured with both a public HTTPS SearXNG
/// base URL and a public HTTPS Ollama base URL at startup. Static — does not
/// change during the lifetime of the process.
/// </summary>
public sealed class StaticCouncilCapabilities : ICouncilCapabilities
{
    public StaticCouncilCapabilities(IOptions<MultiAgentOptions> options)
    {
        IsCouncilEnabled = options.Value.CouncilEnabled;
        DisabledReason = options.Value.DisabledReason;
    }

    public bool IsCouncilEnabled { get; }
    public string DisabledReason { get; }

    // Server-side capabilities are read once at boot; the event never fires.
    public event Action? Changed
    {
        add { }
        remove { }
    }
}
