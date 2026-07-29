namespace Agentic.Chat.Services.MultiAgent;

/// <summary>
/// Per-host capability surface for the 20-Agent Research Council button. Each
/// host (Blazor Server, Blazor WASM on GitHub Pages) registers its own
/// implementation reflecting how it authenticates the multi-agent LLM and
/// search calls. The shared <c>Chat.razor</c> page binds the run control to
/// this contract so it stays host-agnostic and never reaches into
/// browser-only types.
/// </summary>
public interface ICouncilCapabilities
{
    /// <summary>True when the council button should be enabled.</summary>
    bool IsCouncilEnabled { get; }

    /// <summary>Human-readable reason the council cannot run. Empty when enabled; shown as a tooltip.</summary>
    string DisabledReason { get; }

    /// <summary>Raised when <see cref="IsCouncilEnabled"/> or <see cref="DisabledReason"/> change.</summary>
    event Action? Changed;
}
