namespace Agentic.Chat.Services;

public sealed class SystemPromptService(
    BrowserStorage storage,
    ILogger<SystemPromptService> logger)
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
        try
        {
            CurrentPrompt = await _storage.GetLocalAsync<string>(StorageKey).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ClientLog.Warning(logger, exception, "Could not load the system prompt.");
        }
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
        await _storage.SetLocalAsync(StorageKey, trimmed).ConfigureAwait(false);
        CurrentPrompt = trimmed;
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public async Task ClearAsync()
    {
        await _storage.RemoveLocalAsync(StorageKey).ConfigureAwait(false);
        CurrentPrompt = null;
        IsLoaded = true;
        OnChange?.Invoke();
    }
}

public sealed record SystemPromptPreset(string Name, string? Prompt)
{
    public bool ClearsOverride => Prompt is null;
}
