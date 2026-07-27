namespace Agentic.Chat.Services;

public sealed class SelectedModelService(
    BrowserStorage storage,
    OpenRouterCredentialService credentials)
{
    private const string StorageKey = "selected-model";
    private readonly BrowserStorage _storage = storage;
    private readonly OpenRouterCredentialService _credentials = credentials;

    public string? CurrentModelId { get; private set; }

    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public async Task LoadAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        await _credentials.InitializeAsync().ConfigureAwait(false);
        CurrentModelId = await _storage.GetLocalAsync<string>(StorageKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(CurrentModelId)
            || (!_credentials.HasUserKey
                && !OpenRouterCredentialService.IsSharedFreeModel(CurrentModelId)))
        {
            CurrentModelId = "openrouter/free";
        }

        IsLoaded = true;
        OnChange?.Invoke();
    }

    public async Task SetAsync(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        await _credentials.InitializeAsync().ConfigureAwait(false);
        if (!_credentials.HasUserKey
            && !OpenRouterCredentialService.IsSharedFreeModel(modelId))
        {
            throw new InvalidOperationException("Connect your OpenRouter API key to select paid models.");
        }

        CurrentModelId = modelId.Trim();
        await _storage.SetLocalAsync(StorageKey, CurrentModelId).ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }
}
