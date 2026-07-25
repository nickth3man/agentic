using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Agentic.Chat.Services;

/// <summary>
/// Stores model-picker-only preferences independently from the selected model.
/// Persistence is best-effort so an unavailable browser storage API never prevents
/// a model from being favorited or recorded as recently used for this circuit.
/// </summary>
public sealed class ModelPickerPreferencesService
{
    internal const string StorageKey = "model-picker-preferences";
    public const int RecentModelLimit = 5;

    private readonly ProtectedLocalStorage _protectedStore;
    private readonly HashSet<string> _favoriteModelIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recentModelIds = new();

    public ModelPickerPreferencesService(ProtectedLocalStorage protectedStore)
    {
        ArgumentNullException.ThrowIfNull(protectedStore);
        _protectedStore = protectedStore;
    }

    public IReadOnlySet<string> FavoriteModelIds => _favoriteModelIds;

    public IReadOnlyList<string> RecentModelIds => _recentModelIds;

    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public bool IsFavorite(string modelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelId);
        return _favoriteModelIds.Contains(modelId);
    }

    public async Task LoadAsync()
    {
        try
        {
            var stored = await _protectedStore
                .GetAsync<ModelPickerPreferences>(StorageKey)
                .ConfigureAwait(false);
            if (stored.Success && stored.Value is not null)
            {
                ReplacePreferences(stored.Value);
            }
        }
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            // Missing browser interop or unreadable storage leaves preferences empty.
        }
        finally
        {
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    public async Task ToggleFavoriteAsync(string modelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        if (!_favoriteModelIds.Add(modelId))
        {
            _favoriteModelIds.Remove(modelId);
        }

        await PersistAndNotifyAsync();
    }

    public async Task RecordRecentAsync(string modelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        _recentModelIds.RemoveAll(id => string.Equals(id, modelId, StringComparison.OrdinalIgnoreCase));
        _recentModelIds.Insert(0, modelId);
        if (_recentModelIds.Count > RecentModelLimit)
        {
            _recentModelIds.RemoveRange(RecentModelLimit, _recentModelIds.Count - RecentModelLimit);
        }

        await PersistAndNotifyAsync();
    }

    private async Task PersistAndNotifyAsync()
    {
        try
        {
            await _protectedStore
                .SetAsync(StorageKey, new ModelPickerPreferences(_favoriteModelIds.ToList(), _recentModelIds))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            // Persistence failures must not discard the updated in-memory preference.
        }
        finally
        {
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    private void ReplacePreferences(ModelPickerPreferences preferences)
    {
        _favoriteModelIds.Clear();
        foreach (var id in preferences.FavoriteModelIds.Where(static id => !string.IsNullOrWhiteSpace(id)))
        {
            _favoriteModelIds.Add(id);
        }

        _recentModelIds.Clear();
        foreach (var id in preferences.RecentModelIds.Where(static id => !string.IsNullOrWhiteSpace(id)))
        {
            if (_recentModelIds.Any(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _recentModelIds.Add(id);
            if (_recentModelIds.Count == RecentModelLimit)
            {
                break;
            }
        }
    }
}

public sealed record ModelPickerPreferences(
    IReadOnlyList<string> FavoriteModelIds,
    IReadOnlyList<string> RecentModelIds);
