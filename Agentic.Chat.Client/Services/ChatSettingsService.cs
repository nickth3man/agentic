namespace Agentic.Chat.Services;

public sealed class ChatSettingsService(BrowserStorage storage)
{
    private const string StorageKey = "chat-settings";
    public const double MinTemperature = 0d;
    public const double MaxTemperature = 2d;
    public const int MinMaxTokens = 1;
    public const int MaxMaxTokens = 200_000;
    private readonly BrowserStorage _storage = storage;

    public ReasoningEffortLevel ReasoningEffort { get; private set; } = ReasoningEffortLevel.Medium;
    public double? Temperature { get; private set; }
    public int? MaxTokens { get; private set; }
    public bool IsLoaded { get; private set; }
    public event Action? OnChange;

    public async Task LoadAsync()
    {
        if (IsLoaded) { return; }
        var state = await _storage.GetLocalAsync<ChatSettingsState>(StorageKey).ConfigureAwait(false);
        if (state is not null)
        {
            if (Enum.IsDefined(state.ReasoningEffort)) { ReasoningEffort = state.ReasoningEffort; }
            if (state.Temperature is >= MinTemperature and <= MaxTemperature) { Temperature = state.Temperature; }
            if (state.MaxTokens is >= MinMaxTokens and <= MaxMaxTokens) { MaxTokens = state.MaxTokens; }
        }
        IsLoaded = true;
        OnChange?.Invoke();
    }

    public async Task SetReasoningEffortAsync(ReasoningEffortLevel effort)
    {
        ReasoningEffort = effort;
        await PersistAsync().ConfigureAwait(false);
    }

    public async Task SetTemperatureAsync(double? temperature)
    {
        if (temperature is { } value
            && (double.IsNaN(value) || double.IsInfinity(value)
                || value < MinTemperature || value > MaxTemperature))
        {
            throw new ArgumentOutOfRangeException(nameof(temperature));
        }
        Temperature = temperature;
        await PersistAsync().ConfigureAwait(false);
    }

    public async Task SetMaxTokensAsync(int? maxTokens)
    {
        if (maxTokens is { } value && (value < MinMaxTokens || value > MaxMaxTokens))
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens));
        }
        MaxTokens = maxTokens;
        await PersistAsync().ConfigureAwait(false);
    }

    private async Task PersistAsync()
    {
        await _storage
            .SetLocalAsync(StorageKey, new ChatSettingsState(ReasoningEffort, Temperature, MaxTokens))
            .ConfigureAwait(false);
        IsLoaded = true;
        OnChange?.Invoke();
    }

    private sealed record ChatSettingsState(
        ReasoningEffortLevel ReasoningEffort,
        double? Temperature,
        int? MaxTokens);
}

public enum ReasoningEffortLevel
{
    Off,
    Low,
    Medium,
    High
}
