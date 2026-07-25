using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Agentic.Chat.Services;

public sealed class SystemPromptService
{
    internal const string StorageKey = "system-prompt";

    public static IReadOnlyList<SystemPromptPreset> Presets { get; } =
    [
        new("Default", OpenRouterOptions.DefaultSystemPrompt),
        new("Concise", "You are a concise assistant. Prefer short, direct answers without filler."),
        new("Coding", "You are a coding assistant. Prefer correct, idiomatic code with brief explanations."),
        new("Creative", "You are a creative writing partner. Favor vivid language, originality, and engaging tone.")
    ];

    private readonly ProtectedLocalStorage _protectedStore;

    public SystemPromptService(ProtectedLocalStorage protectedStore)
    {
        _protectedStore = protectedStore;
    }

    // Null/empty means "use OpenRouterOptions.SystemPrompt" (config / default).
    public string? CurrentPrompt { get; private set; }

    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public async Task LoadAsync()
    {
        try
        {
            var stored = await _protectedStore.GetAsync<string>(StorageKey).ConfigureAwait(false);
            if (stored.Success && !string.IsNullOrEmpty(stored.Value))
            {
                CurrentPrompt = stored.Value;
            }
        }
        catch (InvalidOperationException)
        {
            // Pre-rendering or other no-JS-yet state: treat as no stored value.
        }
        catch (CryptographicException)
        {
            // Data-protection tampering or wrong purpose: treat as no stored value.
        }
        catch (JsonException)
        {
            // The stored payload wasn't the shape we wrote: treat as no stored value.
        }
        finally
        {
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    public async Task SetAsync(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var trimmed = prompt.Trim();

        try
        {
            await TryPersistAsync(trimmed).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or JsonException)
        {
            // Persistence is best-effort: a JS-not-available (prerender), wrong data-
            // protection purpose, or serialization failure must not lose in-memory
            // state. The finally block below restores the desired final state.
        }
        finally
        {
            CurrentPrompt = trimmed;
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    private ValueTask TryPersistAsync(string prompt)
        => _protectedStore.SetAsync(StorageKey, prompt);

    // Test seam: lets unit tests pin CurrentPrompt without running through the
    // storage. Exposed via Agentic.Chat's InternalsVisibleTo("Agentic.Chat.Tests").
    internal void SetCurrentPromptForTest(string? prompt)
    {
        CurrentPrompt = prompt;
        IsLoaded = true;
        OnChange?.Invoke();
    }
}

public sealed record SystemPromptPreset(string Name, string Prompt);
