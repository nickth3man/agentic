namespace Agentic.Chat.Services;

public sealed class SystemPromptService(BrowserStorage storage)
{
    private const string StorageKey = "system-prompt";
    public const int MaxPromptLength = 8_000;
    private readonly BrowserStorage _storage = storage;

    public static IReadOnlyList<SystemPromptPreset> Presets { get; } =
    [
        new("Default", null),
        new("Concise", "You are a concise assistant. Prefer short, direct answers without filler."),
        new("Coding", "You are a coding assistant. Prefer correct, idiomatic code with brief explanations."),
        new("Creative", "You are a creative writing partner. Favor vivid language, originality, and engaging tone.")
    ];

    public string? CurrentPrompt { get; private set; }
    public bool IsLoaded { get; private set; }
    public event Action? OnChange;

    public async Task LoadAsync()
    {
        if (IsLoaded) { return; }
        CurrentPrompt = await _storage.GetLocalAsync<string>(StorageKey).ConfigureAwait(false);
        if (CurrentPrompt?.Length > MaxPromptLength)
        {
            CurrentPrompt = CurrentPrompt[..MaxPromptLength];
        }
        IsLoaded = true;
        OnChange?.Invoke();
    }

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
        CurrentPrompt = trimmed;
        await _storage.SetLocalAsync(StorageKey, trimmed).ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public async Task ClearAsync()
    {
        CurrentPrompt = null;
        await _storage.RemoveLocalAsync(StorageKey).ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }
}

public sealed record SystemPromptPreset(string Name, string? Prompt)
{
    public bool ClearsOverride => Prompt is null;
}
