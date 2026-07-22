using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Agentic.Chat.Services;

public sealed class SelectedModelService
{
    internal const string StorageKey = "selected-model";

    private readonly ProtectedLocalStorage _protectedStore;

    public SelectedModelService(ProtectedLocalStorage protectedStore)
    {
        _protectedStore = protectedStore;
    }

    public string? CurrentModelId { get; private set; }

    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public async Task LoadAsync()
    {
        try
        {
            var stored = await _protectedStore.GetAsync<string>(StorageKey).ConfigureAwait(false);
            if (stored.Success && !string.IsNullOrEmpty(stored.Value))
            {
                CurrentModelId = stored.Value;
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

    public async Task SetAsync(string modelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        try
        {
            await TryPersistAsync(modelId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or JsonException)
        {
            // Persistence is best-effort: a JS-not-available (prerender), wrong data-
            // protection purpose, or serialization failure must not lose in-memory
            // state. The finally block below restores the desired final state.
        }
        finally
        {
            CurrentModelId = modelId;
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    private ValueTask TryPersistAsync(string modelId)
        => _protectedStore.SetAsync(StorageKey, modelId);

    // Test seam: lets unit tests pin CurrentModelId without running through the
    // storage. Exposed via Agentic.Chat's InternalsVisibleTo("Agentic.Chat.Tests").
    internal void SetCurrentModelIdForTest(string? id)
    {
        CurrentModelId = id;
        IsLoaded = true;
        OnChange?.Invoke();
    }
}
