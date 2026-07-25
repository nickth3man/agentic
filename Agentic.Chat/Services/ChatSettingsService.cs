using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Agentic.Chat.Services;

/// <summary>
/// Persisted chat generation settings (reasoning effort, temperature, max tokens).
/// Defaults match prior behavior: reasoning on (medium) for supporting models,
/// no temperature/max_tokens until the user sets them.
/// </summary>
public sealed class ChatSettingsService
{
    internal const string StorageKey = "chat-settings";

    public const double MinTemperature = 0d;
    public const double MaxTemperature = 2d;
    public const int MinMaxTokens = 1;
    public const int MaxMaxTokens = 200_000;

    private readonly ProtectedLocalStorage _protectedStore;

    public ChatSettingsService(ProtectedLocalStorage protectedStore)
    {
        ArgumentNullException.ThrowIfNull(protectedStore);
        _protectedStore = protectedStore;
    }

    /// <summary>Default <see cref="ReasoningEffortLevel.Medium"/> keeps reasoning enabled.</summary>
    public ReasoningEffortLevel ReasoningEffort { get; private set; } = ReasoningEffortLevel.Medium;

    /// <summary>Null means omit (provider default).</summary>
    public double? Temperature { get; private set; }

    /// <summary>Null means omit (provider default).</summary>
    public int? MaxTokens { get; private set; }

    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public async Task LoadAsync()
    {
        try
        {
            var stored = await _protectedStore.GetAsync<ChatSettingsState>(StorageKey).ConfigureAwait(false);
            if (stored.Success && stored.Value is { } state)
            {
                ApplyState(state);
            }
        }
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            // Pre-rendering / crypto / shape / JS failures: keep defaults.
        }
        finally
        {
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    public async Task SetReasoningEffortAsync(ReasoningEffortLevel effort)
    {
        ReasoningEffort = effort;
        await PersistAsync().ConfigureAwait(false);
    }

    public async Task SetTemperatureAsync(double? temperature)
    {
        if (temperature is { } t)
        {
            if (double.IsNaN(t) || double.IsInfinity(t) || t < MinTemperature || t > MaxTemperature)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(temperature),
                    $"Temperature must be between {MinTemperature} and {MaxTemperature}.");
            }
        }

        Temperature = temperature;
        await PersistAsync().ConfigureAwait(false);
    }

    public async Task SetMaxTokensAsync(int? maxTokens)
    {
        if (maxTokens is { } n && (n < MinMaxTokens || n > MaxMaxTokens))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTokens),
                $"Max tokens must be between {MinMaxTokens} and {MaxMaxTokens}.");
        }

        MaxTokens = maxTokens;
        await PersistAsync().ConfigureAwait(false);
    }

    private async Task PersistAsync()
    {
        try
        {
            await _protectedStore.SetAsync(StorageKey, Snapshot()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ProtectedStorageHelpers.IsBestEffortPersistenceFailure(ex))
        {
            // Persistence is best-effort.
        }
        finally
        {
            IsLoaded = true;
            OnChange?.Invoke();
        }
    }

    private ChatSettingsState Snapshot() => new(ReasoningEffort, Temperature, MaxTokens);

    private void ApplyState(ChatSettingsState state)
    {
        if (Enum.IsDefined(state.ReasoningEffort))
        {
            ReasoningEffort = state.ReasoningEffort;
        }

        if (state.Temperature is { } t
            && t >= MinTemperature
            && t <= MaxTemperature)
        {
            Temperature = t;
        }

        if (state.MaxTokens is { } n
            && n >= MinMaxTokens
            && n <= MaxMaxTokens)
        {
            MaxTokens = n;
        }
    }

    // Test seam via InternalsVisibleTo.
    internal void SetForTest(
        ReasoningEffortLevel effort = ReasoningEffortLevel.Medium,
        double? temperature = null,
        int? maxTokens = null)
    {
        ReasoningEffort = effort;
        Temperature = temperature;
        MaxTokens = maxTokens;
        IsLoaded = true;
        OnChange?.Invoke();
    }

    internal sealed record ChatSettingsState(
        ReasoningEffortLevel ReasoningEffort,
        double? Temperature,
        int? MaxTokens);
}

public enum ReasoningEffortLevel
{
    Off = 0,
    Low = 1,
    Medium = 2,
    High = 3
}
