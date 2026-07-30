using System.Text;
using System.Text.Json;
using Agentic.Chat.Services;
using Agentic.Chat.Tests.Fixtures;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;

namespace Agentic.Chat.Tests;

public class ChatSettingsServiceTests : IClassFixture<ProtectedBrowserStorageFixture>
{
    private readonly ProtectedBrowserStorageFixture _fixture;

    public ChatSettingsServiceTests(ProtectedBrowserStorageFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoadAsync_WithNoStoredValue_KeepsDefaults()
    {
        var (service, _) = BuildService(seed: null);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Equal(ReasoningEffortLevel.Medium, service.ReasoningEffort);
        Assert.Null(service.Temperature);
        Assert.Null(service.MaxTokens);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task RoundTrip_PersistsEffortTemperatureAndMaxTokens()
    {
        var js = _fixture.CreateRuntime();
        var sharedDp = new EphemeralDataProtectionProvider();
        var initial = new ChatSettingsService(_fixture.CreateStorage(js, sharedDp));
        await initial.SetReasoningEffortAsync(ReasoningEffortLevel.High);
        await initial.SetTemperatureAsync(0.7);
        await initial.SetMaxTokensAsync(1024);

        var fresh = new ChatSettingsService(_fixture.CreateStorage(js, sharedDp));
        await fresh.LoadAsync();

        Assert.Equal(ReasoningEffortLevel.High, fresh.ReasoningEffort);
        Assert.Equal(0.7, fresh.Temperature);
        Assert.Equal(1024, fresh.MaxTokens);
    }

    [Fact]
    public async Task SetTemperatureAsync_Null_ClearsOverride()
    {
        var (service, _) = BuildService();
        await service.SetTemperatureAsync(1.1);
        await service.SetTemperatureAsync(null);
        Assert.Null(service.Temperature);
    }

    [Fact]
    public async Task SetMaxTokensAsync_Null_ClearsOverride()
    {
        var (service, _) = BuildService();
        await service.SetMaxTokensAsync(256);
        await service.SetMaxTokensAsync(null);
        Assert.Null(service.MaxTokens);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task SetTemperatureAsync_OutOfRange_Throws(double value)
    {
        var (service, _) = BuildService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetTemperatureAsync(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200_001)]
    public async Task SetMaxTokensAsync_OutOfRange_Throws(int value)
    {
        var (service, _) = BuildService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetMaxTokensAsync(value));
    }

    [Fact]
    public async Task LoadAsync_OnNoJsInterop_Swallows_IsLoadedTrue()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateNoInteropRuntime(),
            new EphemeralDataProtectionProvider());
        var service = new ChatSettingsService(storage);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Equal(ReasoningEffortLevel.Medium, service.ReasoningEffort);
    }

    [Fact]
    public async Task LoadAsync_OnCorruptedStorage_Swallows_IsLoadedTrue()
    {
        var seedBytes = Encoding.UTF8.GetBytes("not a JSON object");
        var seedProtected = Convert.ToBase64String(seedBytes);
        var js = _fixture.CreateRuntime(new Dictionary<string, string>
        {
            [ChatSettingsService.StorageKey] = seedProtected
        });
        var storage = _fixture.CreateStorage(js, _fixture.CreateIdentityProtector());
        var service = new ChatSettingsService(storage);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Equal(ReasoningEffortLevel.Medium, service.ReasoningEffort);
    }

    [Fact]
    public async Task LoadAsync_IgnoresOutOfRangePersistedValues()
    {
        var js = _fixture.CreateRuntime();
        var dp = new EphemeralDataProtectionProvider();
        var storage = _fixture.CreateStorage(js, dp);
        var bad = new ChatSettingsService.ChatSettingsState(
            (ReasoningEffortLevel)999,
            9.0,
            -5);
        await storage.SetAsync(ChatSettingsService.StorageKey, bad);

        var service = new ChatSettingsService(storage);
        await service.LoadAsync();

        Assert.Equal(ReasoningEffortLevel.Medium, service.ReasoningEffort);
        Assert.Null(service.Temperature);
        Assert.Null(service.MaxTokens);
    }

    [Fact]
    public async Task LoadAsync_IgnoresTemperatureBelowMin()
    {
        var js = _fixture.CreateRuntime();
        var dp = new EphemeralDataProtectionProvider();
        var storage = _fixture.CreateStorage(js, dp);
        await storage.SetAsync(
            ChatSettingsService.StorageKey,
            new ChatSettingsService.ChatSettingsState(ReasoningEffortLevel.Medium, -0.01, null));

        var service = new ChatSettingsService(storage);
        await service.LoadAsync();
        Assert.Null(service.Temperature);
        Assert.Null(service.MaxTokens);
    }

    [Fact]
    public async Task LoadAsync_IgnoresMaxTokensAboveMax()
    {
        var js = _fixture.CreateRuntime();
        var dp = new EphemeralDataProtectionProvider();
        var storage = _fixture.CreateStorage(js, dp);
        await storage.SetAsync(
            ChatSettingsService.StorageKey,
            new ChatSettingsService.ChatSettingsState(
                ReasoningEffortLevel.Medium,
                null,
                ChatSettingsService.MaxMaxTokens + 1));

        var service = new ChatSettingsService(storage);
        await service.LoadAsync();
        Assert.Null(service.Temperature);
        Assert.Null(service.MaxTokens);
    }

    [Fact]
    public async Task LoadAsync_AppliesTemperatureOnly_WhenMaxTokensNull()
    {
        var js = _fixture.CreateRuntime();
        var dp = new EphemeralDataProtectionProvider();
        var storage = _fixture.CreateStorage(js, dp);
        await storage.SetAsync(
            ChatSettingsService.StorageKey,
            new ChatSettingsService.ChatSettingsState(ReasoningEffortLevel.High, 1.25, null));

        var service = new ChatSettingsService(storage);
        await service.LoadAsync();
        Assert.Equal(1.25, service.Temperature);
        Assert.Null(service.MaxTokens);
    }

    [Fact]
    public async Task LoadAsync_AppliesMaxTokensOnly_WhenTemperatureNull()
    {
        var js = _fixture.CreateRuntime();
        var dp = new EphemeralDataProtectionProvider();
        var storage = _fixture.CreateStorage(js, dp);
        await storage.SetAsync(
            ChatSettingsService.StorageKey,
            new ChatSettingsService.ChatSettingsState(
                ReasoningEffortLevel.Low,
                null,
                ChatSettingsService.MinMaxTokens));

        var service = new ChatSettingsService(storage);
        await service.LoadAsync();
        Assert.Null(service.Temperature);
        Assert.Equal(ChatSettingsService.MinMaxTokens, service.MaxTokens);
    }

    [Fact]
    public async Task LoadAsync_AppliesValidBoundaryValues()
    {
        var js = _fixture.CreateRuntime();
        var dp = new EphemeralDataProtectionProvider();
        var storage = _fixture.CreateStorage(js, dp);
        var state = new ChatSettingsService.ChatSettingsState(
            ReasoningEffortLevel.Low,
            ChatSettingsService.MinTemperature,
            ChatSettingsService.MaxMaxTokens);
        await storage.SetAsync(ChatSettingsService.StorageKey, state);

        var service = new ChatSettingsService(storage);
        await service.LoadAsync();

        Assert.Equal(ReasoningEffortLevel.Low, service.ReasoningEffort);
        Assert.Equal(ChatSettingsService.MinTemperature, service.Temperature);
        Assert.Equal(ChatSettingsService.MaxMaxTokens, service.MaxTokens);
    }

    [Fact]
    public async Task PersistAsync_WithNoOnChangeSubscriber_StillSucceeds()
    {
        var (service, _) = BuildService();
        // No OnChange handler subscribed — covers OnChange?.Invoke null branch.
        await service.SetReasoningEffortAsync(ReasoningEffortLevel.High);
        Assert.Equal(ReasoningEffortLevel.High, service.ReasoningEffort);
    }

    [Fact]
    public async Task PersistAsync_WithSubscriber_RaisesOnChange()
    {
        var (service, _) = BuildService();
        var changes = 0;
        service.OnChange += () => changes++;

        await service.SetTemperatureAsync(0.55);

        Assert.Equal(0.55, service.Temperature);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task SetReasoningEffortAsync_OnPersistFailure_KeepsInMemoryValue()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateNoInteropRuntime(),
            new EphemeralDataProtectionProvider());
        var service = new ChatSettingsService(storage);

        await service.SetReasoningEffortAsync(ReasoningEffortLevel.Low);

        Assert.Equal(ReasoningEffortLevel.Low, service.ReasoningEffort);
        Assert.True(service.IsLoaded);
    }

    [Fact]
    public void SetForTest_PinsValuesAndRaisesOnChange()
    {
        var (service, _) = BuildService();
        var changes = 0;
        service.OnChange += () => changes++;

        service.SetForTest(ReasoningEffortLevel.Off, 0.2, 128);

        Assert.Equal(ReasoningEffortLevel.Off, service.ReasoningEffort);
        Assert.Equal(0.2, service.Temperature);
        Assert.Equal(128, service.MaxTokens);
        Assert.True(service.IsLoaded);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Ctor_NullStorage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatSettingsService(null!));
    }

    private (ChatSettingsService Service, TestSupport.ProtectedJSRuntime Store) BuildService(
        Dictionary<string, string>? seed = null)
    {
        var js = _fixture.CreateRuntime(seed);
        var storage = _fixture.CreateStorage(js, new EphemeralDataProtectionProvider());
        return (new ChatSettingsService(storage), js);
    }
}
