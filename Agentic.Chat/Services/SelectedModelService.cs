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
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            // Pre-rendering / crypto / shape / JS failures: treat as no stored value.
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
            await _protectedStore.SetAsync(StorageKey, modelId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            // Persistence is best-effort: prerender, crypto, serialization, or
            // JS failure must not lose in-memory state.
        }
        finally
        {
            CurrentModelId = modelId;
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    // Test seam: lets unit tests pin CurrentModelId without running through the
    // storage. Exposed via Agentic.Chat's InternalsVisibleTo("Agentic.Chat.Tests").
    internal void SetCurrentModelIdForTest(string? id)
    {
        CurrentModelId = id;
        IsLoaded = true;
        OnChange?.Invoke();
    }
}
