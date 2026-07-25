using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;

namespace Agentic.Chat.Tests;

public class ModelPickerPreferencesServiceTests
{
    [Fact]
    public async Task LoadAsync_WithStoredPreferences_NormalizesAndRaisesChange()
    {
        var js = TestSupport.NewProtectedJSRuntime();
        var dataProtection = new EphemeralDataProtectionProvider();
        var storage = BuildStorage(js, dataProtection);
        await storage.SetAsync(
            ModelPickerPreferencesService.StorageKey,
            new ModelPickerPreferences(
                ["openai/gpt-4o", "OPENAI/GPT-4O", " ", "anthropic/claude"],
                ["first", "FIRST", " ", "second", "third", "fourth", "fifth", "sixth"]));

        var service = new ModelPickerPreferencesService(BuildStorage(js, dataProtection));
        var changes = 0;
        service.OnChange += () => changes++;

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Equal(["anthropic/claude", "openai/gpt-4o"], service.FavoriteModelIds.Order());
        Assert.Equal(["first", "second", "third", "fourth", "fifth"], service.RecentModelIds);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task LoadAsync_WithNoStoredPreferences_LeavesCollectionsEmpty()
    {
        var service = new ModelPickerPreferencesService(BuildStorage(TestSupport.NewProtectedJSRuntime()));

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Empty(service.FavoriteModelIds);
        Assert.Empty(service.RecentModelIds);
    }

    [Fact]
    public async Task LoadAsync_OnStorageFailure_LeavesCollectionsEmpty()
    {
        var service = new ModelPickerPreferencesService(
            new ProtectedLocalStorage(new NoInteropJSRuntime(), new EphemeralDataProtectionProvider()));

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Empty(service.FavoriteModelIds);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_AddsThenRemovesAndPersists()
    {
        var js = TestSupport.NewProtectedJSRuntime();
        var dataProtection = new EphemeralDataProtectionProvider();
        var service = new ModelPickerPreferencesService(BuildStorage(js, dataProtection));
        var changes = 0;
        service.OnChange += () => changes++;

        await service.ToggleFavoriteAsync("openai/gpt-4o");

        Assert.True(service.IsFavorite("OPENAI/GPT-4O"));
        Assert.True(js.Store.ContainsKey(ModelPickerPreferencesService.StorageKey));

        await service.ToggleFavoriteAsync("openai/gpt-4o");

        var reloaded = new ModelPickerPreferencesService(BuildStorage(js, dataProtection));
        await reloaded.LoadAsync();
        Assert.False(reloaded.IsFavorite("openai/gpt-4o"));
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task RecordRecentAsync_MovesDuplicateToFrontAndKeepsFive()
    {
        var service = new ModelPickerPreferencesService(BuildStorage(TestSupport.NewProtectedJSRuntime()));

        foreach (var id in new[] { "one", "two", "three", "four", "five", "six", "three" })
        {
            await service.RecordRecentAsync(id);
        }

        Assert.Equal(["three", "six", "five", "four", "two"], service.RecentModelIds);
        Assert.Equal(ModelPickerPreferencesService.RecentModelLimit, service.RecentModelIds.Count);
    }

    [Fact]
    public async Task PersistFailure_StillUpdatesMemoryAndRaisesChange()
    {
        var service = new ModelPickerPreferencesService(
            new ProtectedLocalStorage(new NoInteropJSRuntime(), new EphemeralDataProtectionProvider()));
        var changes = 0;
        service.OnChange += () => changes++;

        await service.ToggleFavoriteAsync("openai/gpt-4o");
        await service.RecordRecentAsync("openai/gpt-4o");

        Assert.True(service.IsLoaded);
        Assert.True(service.IsFavorite("openai/gpt-4o"));
        Assert.Equal(["openai/gpt-4o"], service.RecentModelIds);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task ModelIds_CannotBeNullOrEmpty()
    {
        var service = new ModelPickerPreferencesService(BuildStorage(TestSupport.NewProtectedJSRuntime()));

        Assert.Throws<ArgumentNullException>(() => service.IsFavorite(null!));
        Assert.Throws<ArgumentException>(() => service.IsFavorite(string.Empty));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ToggleFavoriteAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ToggleFavoriteAsync(string.Empty));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RecordRecentAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordRecentAsync(string.Empty));
    }

    [Fact]
    public void Constructor_NullStorage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ModelPickerPreferencesService(null!));
    }

    private static ProtectedLocalStorage BuildStorage(
        TestSupport.ProtectedJSRuntime store,
        IDataProtectionProvider? dataProtection = null)
        => new(store, dataProtection ?? new EphemeralDataProtectionProvider());

    private sealed class NoInteropJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("JS interop is not available in this test.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new InvalidOperationException("JS interop is not available in this test.");
    }
}
