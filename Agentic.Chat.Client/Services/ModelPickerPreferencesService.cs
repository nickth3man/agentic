namespace Agentic.Chat.Services;

public sealed class ModelPickerPreferencesService(BrowserStorage storage)
{
    private const string StorageKey = "model-picker-preferences";
    public const int RecentModelLimit = 5;
    private readonly BrowserStorage _storage = storage;
    private readonly HashSet<string> _favoriteModelIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recentModelIds = [];

    public IReadOnlySet<string> FavoriteModelIds => _favoriteModelIds;
    public IReadOnlyList<string> RecentModelIds => _recentModelIds;
    public bool IsLoaded { get; private set; }
    public event Action? OnChange;

    public bool IsFavorite(string modelId) => _favoriteModelIds.Contains(modelId);

    public async Task LoadAsync()
    {
        if (IsLoaded) { return; }
        var value = await _storage.GetLocalAsync<ModelPickerPreferences>(StorageKey).ConfigureAwait(false);
        if (value is not null)
        {
            foreach (var id in value.FavoriteModelIds.Where(static id => !string.IsNullOrWhiteSpace(id)))
            {
                _favoriteModelIds.Add(id);
            }
            foreach (var id in value.RecentModelIds.Where(static id => !string.IsNullOrWhiteSpace(id)))
            {
                if (!_recentModelIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    _recentModelIds.Add(id);
                }
                if (_recentModelIds.Count == RecentModelLimit) { break; }
            }
        }
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public async Task ToggleFavoriteAsync(string modelId)
    {
        if (!_favoriteModelIds.Add(modelId)) { _favoriteModelIds.Remove(modelId); }
        await PersistAsync().ConfigureAwait(false);
    }

    public async Task RecordRecentAsync(string modelId)
    {
        _recentModelIds.RemoveAll(id => string.Equals(id, modelId, StringComparison.OrdinalIgnoreCase));
        _recentModelIds.Insert(0, modelId);
        if (_recentModelIds.Count > RecentModelLimit)
        {
            _recentModelIds.RemoveRange(RecentModelLimit, _recentModelIds.Count - RecentModelLimit);
        }
        await PersistAsync().ConfigureAwait(false);
    }

    private async Task PersistAsync()
    {
        await _storage.SetLocalAsync(
            StorageKey,
            new ModelPickerPreferences(_favoriteModelIds.ToList(), _recentModelIds)).ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }
}

public sealed record ModelPickerPreferences(
    IReadOnlyList<string> FavoriteModelIds,
    IReadOnlyList<string> RecentModelIds);
