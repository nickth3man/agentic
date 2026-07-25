using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.Logging;

namespace Agentic.Chat.Services;

public sealed class SystemPromptService
{
    internal const string StorageKey = "system-prompt";

    /// <summary>Upper bound on persisted / applied UI prompt length (characters).</summary>
    public const int MaxPromptLength = 8_000;

    // "Default" uses a null Prompt: selecting it clears the UI override so
    // OpenRouter:SystemPrompt / OpenRouterOptions.DefaultSystemPrompt win.
    public static IReadOnlyList<SystemPromptPreset> Presets { get; } =
    [
        new("Default", null),
        new("Concise", "You are a concise assistant. Prefer short, direct answers without filler."),
        new("Coding", "You are a coding assistant. Prefer correct, idiomatic code with brief explanations."),
        new("Creative", "You are a creative writing partner. Favor vivid language, originality, and engaging tone.")
    ];

    private static readonly Action<ILogger, string, Exception?> LogPersistenceFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            default,
            "System prompt persistence {Operation} failed; continuing with in-memory state");

    private readonly ProtectedLocalStorage _protectedStore;
    private readonly ILogger<SystemPromptService> _logger;

    public SystemPromptService(
        ProtectedLocalStorage protectedStore,
        ILogger<SystemPromptService> logger)
    {
        ArgumentNullException.ThrowIfNull(protectedStore);
        ArgumentNullException.ThrowIfNull(logger);
        _protectedStore = protectedStore;
        _logger = logger;
    }

    // Null means "use OpenRouterOptions.SystemPrompt" (config / default).
    public string? CurrentPrompt { get; private set; }

    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    /// <summary>
    /// Loads the persisted UI override from protected local storage.
    /// Missing, empty, or unreadable values leave <see cref="CurrentPrompt"/> null
    /// so configuration wins. Failures are best-effort (logged without prompt content).
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            var stored = await _protectedStore.GetAsync<string>(StorageKey).ConfigureAwait(false);
            if (stored.Success && !string.IsNullOrEmpty(stored.Value))
            {
                CurrentPrompt = TruncateIfNeeded(stored.Value);
            }
        }
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            // Pre-rendering / JS / crypto / shape failures: treat as no stored value.
            LogPersistenceFailure(_logger, "load", ex);
        }
        finally
        {
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    /// <summary>
    /// Sets the UI override (trimmed), best-effort persists it, and raises
    /// <see cref="OnChange"/>. Rejects whitespace and prompts longer than
    /// <see cref="MaxPromptLength"/>. Persistence failures keep the in-memory value.
    /// </summary>
    public async Task SetAsync(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var trimmed = prompt.Trim();
        if (trimmed.Length > MaxPromptLength)
        {
            throw new ArgumentException(
                $"System prompt must be at most {MaxPromptLength} characters.",
                nameof(prompt));
        }

        try
        {
            await _protectedStore.SetAsync(StorageKey, trimmed).ConfigureAwait(false);
        }
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            // Persistence is best-effort: prerender, crypto, serialization, or
            // JSException must not lose in-memory state.
            LogPersistenceFailure(_logger, "set", ex);
        }
        finally
        {
            CurrentPrompt = trimmed;
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    /// <summary>
    /// Clears the UI override (and deletes the stored value when possible) so
    /// <see cref="OpenRouterOptions.SystemPrompt"/> is used again.
    /// </summary>
    public async Task ClearAsync()
    {
        try
        {
            await _protectedStore.DeleteAsync(StorageKey).ConfigureAwait(false);
        }
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            LogPersistenceFailure(_logger, "clear", ex);
        }
        finally
        {
            CurrentPrompt = null;
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    private static string TruncateIfNeeded(string value)
        => value.Length <= MaxPromptLength ? value : value[..MaxPromptLength];

    // Test seam: lets unit tests pin CurrentPrompt without running through the
    // storage. Exposed via Agentic.Chat's InternalsVisibleTo("Agentic.Chat.Tests").
    internal void SetCurrentPromptForTest(string? prompt)
    {
        CurrentPrompt = prompt;
        IsLoaded = true;
        OnChange?.Invoke();
    }
}

/// <param name="Name">Display name in the preset dropdown.</param>
/// <param name="Prompt">
/// Preset text, or <see langword="null"/> for the Default preset (clear override /
/// use configuration).
/// </param>
public sealed record SystemPromptPreset(string Name, string? Prompt)
{
    /// <summary>
    /// When true, applying this preset clears the UI override so configuration wins.
    /// </summary>
    public bool ClearsOverride => Prompt is null;
}
