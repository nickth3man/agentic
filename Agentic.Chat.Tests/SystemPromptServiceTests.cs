using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Services;
using Agentic.Chat.Tests.Fixtures;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Agentic.Chat.Tests;

// Storage-layer tests for SystemPromptService. Mirrors SelectedModelServiceTests:
// real ProtectedLocalStorage + TestSupport.ProtectedJSRuntime, with dedicated
// IDataProtectionProvider fakes to hit each LoadAsync/SetAsync catch branch.
public class SystemPromptServiceTests : IClassFixture<ProtectedBrowserStorageFixture>
{
    private readonly ProtectedBrowserStorageFixture _fixture;

    public SystemPromptServiceTests(ProtectedBrowserStorageFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoadAsync_WithNoStoredValue_SetsIsLoadedTrue_CurrentPromptNull()
    {
        var (service, store) = BuildService(seed: null);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentPrompt);
        Assert.Empty(store.Store);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task LoadAsync_WithStoredValue_SetsCurrentPrompt_RaisesOnChange()
    {
        var js = _fixture.CreateRuntime();
        var sharedDp = new EphemeralDataProtectionProvider();
        var initial = new SystemPromptService(_fixture.CreateStorage(js, sharedDp), NullLogger<SystemPromptService>.Instance);
        await initial.SetAsync("Stored system prompt.");
        Assert.True(js.Store.ContainsKey(SystemPromptService.StorageKey));

        var fresh = new SystemPromptService(_fixture.CreateStorage(js, sharedDp), NullLogger<SystemPromptService>.Instance);
        var changes = 0;
        fresh.OnChange += () => changes++;

        await fresh.LoadAsync();

        Assert.True(fresh.IsLoaded);
        Assert.Equal("Stored system prompt.", fresh.CurrentPrompt);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task LoadAsync_OnNoJsInterop_SwallowsInvalidOperationException_IsLoadedTrue()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateNoInteropRuntime(),
            new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentPrompt);
    }

    [Fact]
    public async Task LoadAsync_OnJSException_Swallows_IsLoadedTrue()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateThrowingRuntime(new JSException("synthetic load failure")),
            new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentPrompt);
    }

    [Fact]
    public async Task LoadAsync_OnCorruptedStorage_SwallowsJsonException_IsLoadedTrue()
    {
        var seedBytes = Encoding.UTF8.GetBytes("not a JSON string");
        var seedProtected = Convert.ToBase64String(seedBytes);
        var js = _fixture.CreateRuntime(new Dictionary<string, string>
        {
            [SystemPromptService.StorageKey] = seedProtected
        });

        var storage = _fixture.CreateStorage(js, _fixture.CreateIdentityProtector());
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentPrompt);
    }

    [Fact]
    public async Task LoadAsync_OnWrongProtectedProvider_SwallowsCryptographicException_IsLoadedTrue()
    {
        var js = _fixture.CreateRuntime();
        var writer = new SystemPromptService(
            _fixture.CreateStorage(js),
            NullLogger<SystemPromptService>.Instance);
        await writer.SetAsync("A prompt.");
        Assert.True(js.Store.ContainsKey(SystemPromptService.StorageKey));

        var reader = new SystemPromptService(
            _fixture.CreateStorage(js),
            NullLogger<SystemPromptService>.Instance);
        var before = reader.CurrentPrompt;

        await reader.LoadAsync();

        Assert.True(reader.IsLoaded);
        Assert.Equal(before, reader.CurrentPrompt);
    }

    [Fact]
    public async Task LoadAsync_WithStoredEmptyString_LeavesCurrentPromptNull()
    {
        var seedBytes = Encoding.UTF8.GetBytes("\"\"");
        var seedProtected = Convert.ToBase64String(seedBytes);
        var js = _fixture.CreateRuntime(new Dictionary<string, string>
        {
            [SystemPromptService.StorageKey] = seedProtected
        });

        var service = new SystemPromptService(
            _fixture.CreateStorage(js, _fixture.CreateIdentityProtector()),
            NullLogger<SystemPromptService>.Instance);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentPrompt);
    }

    [Fact]
    public async Task SetAsync_PersistsValue_UpdatesCurrentPrompt_RaisesOnChange()
    {
        var (service, store) = BuildService(seed: null);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.SetAsync("First prompt.");
        Assert.Equal("First prompt.", service.CurrentPrompt);
        Assert.True(store.Store.ContainsKey(SystemPromptService.StorageKey));
        Assert.Equal(1, changes);

        await service.SetAsync("  Second prompt.  ");
        Assert.Equal("Second prompt.", service.CurrentPrompt);
        Assert.True(service.IsLoaded);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task SetAsync_OnStorageFailure_StillUpdatesInMemory_RaisesOnChange()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateNoInteropRuntime(),
            new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.SetAsync("In-memory only prompt.");

        Assert.True(service.IsLoaded);
        Assert.Equal("In-memory only prompt.", service.CurrentPrompt);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task SetAsync_OnJSException_StillUpdatesInMemory()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateThrowingRuntime(new JSException("synthetic js failure")),
            new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);

        await service.SetAsync("Survives JSException.");

        Assert.Equal("Survives JSException.", service.CurrentPrompt);
        Assert.True(service.IsLoaded);
    }

    [Fact]
    public async Task SetAsync_OnUnexpectedException_Propagates()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateThrowingRuntime(new IOException("not a best-effort failure")),
            new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);

        await Assert.ThrowsAsync<IOException>(() => service.SetAsync("Should not stick via catch."));
    }

    [Fact]
    public async Task SetAsync_NullOrWhitespace_Throws()
    {
        var (service, _) = BuildService(seed: null);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SetAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("   "));
    }

    [Fact]
    public async Task SetAsync_ExceedingMaxLength_Throws()
    {
        var (service, _) = BuildService(seed: null);
        var tooLong = new string('x', SystemPromptService.MaxPromptLength + 1);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(tooLong));
        Assert.Null(service.CurrentPrompt);
    }

    [Fact]
    public async Task SetAsync_OnCryptographicExceptionOnProtect_Swallows_StillUpdatesInMemory()
    {
        var js = _fixture.CreateRuntime();
        var faultyDp = _fixture.CreateFaultyProtector(new CryptographicException("synthetic"));
        var storage = _fixture.CreateStorage(js, faultyDp);
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.SetAsync("Prompt despite crypto failure.");

        Assert.True(service.IsLoaded);
        Assert.Equal("Prompt despite crypto failure.", service.CurrentPrompt);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task SetAsync_OnJsonExceptionFromProtect_Swallows_StillUpdatesInMemory()
    {
        var js = _fixture.CreateRuntime();
        var faultyDp = _fixture.CreateFaultyProtector(new JsonException("synthetic"));
        var storage = _fixture.CreateStorage(js, faultyDp);
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);

        await service.SetAsync("Prompt despite json failure.");

        Assert.True(service.IsLoaded);
        Assert.Equal("Prompt despite json failure.", service.CurrentPrompt);
    }

    [Fact]
    public async Task ClearAsync_RemovesStoredValue_AndNullsCurrentPrompt()
    {
        var (service, store) = BuildService(seed: null);
        await service.SetAsync("Override prompt.");
        Assert.True(store.Store.ContainsKey(SystemPromptService.StorageKey));

        var changes = 0;
        service.OnChange += () => changes++;
        await service.ClearAsync();

        Assert.Null(service.CurrentPrompt);
        Assert.True(service.IsLoaded);
        Assert.False(store.Store.ContainsKey(SystemPromptService.StorageKey));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ClearAsync_OnStorageFailure_StillNullsInMemory()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateNoInteropRuntime(),
            new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);
        service.SetCurrentPromptForTest("Sticky override.");

        await service.ClearAsync();

        Assert.Null(service.CurrentPrompt);
        Assert.True(service.IsLoaded);
    }

    [Fact]
    public async Task LoadAsync_WithOversizedStoredValue_TruncatesToMaxLength()
    {
        var oversized = new string('y', SystemPromptService.MaxPromptLength + 50);
        var js = _fixture.CreateRuntime();
        var sharedDp = new EphemeralDataProtectionProvider();
        // Bypass SetAsync length guard by writing via the protected store directly.
        await _fixture.CreateStorage(js, sharedDp).SetAsync(SystemPromptService.StorageKey, oversized);

        var reader = new SystemPromptService(_fixture.CreateStorage(js, sharedDp), NullLogger<SystemPromptService>.Instance);
        await reader.LoadAsync();

        Assert.True(reader.IsLoaded);
        Assert.Equal(SystemPromptService.MaxPromptLength, reader.CurrentPrompt!.Length);
    }

    [Fact]
    public async Task ClearAsync_OnJsException_StillClearsInMemory()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateThrowingRuntime(new JSException("synthetic localStorage failure")),
            new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance);
        service.SetCurrentPromptForTest("UI override.");

        await service.ClearAsync();

        Assert.Null(service.CurrentPrompt);
        Assert.True(service.IsLoaded);
    }

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        var storage = _fixture.CreateStorage();

        Assert.Throws<ArgumentNullException>(() =>
            new SystemPromptService(null!, NullLogger<SystemPromptService>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemPromptService(storage, null!));
    }

    [Fact]
    public void SetCurrentPromptForTest_UpdatesStateAndRaisesOnChange()
    {
        var js = _fixture.CreateRuntime();
        var service = new SystemPromptService(_fixture.CreateStorage(js), NullLogger<SystemPromptService>.Instance);
        var changes = 0;
        service.OnChange += () => changes++;

        service.SetCurrentPromptForTest("Pinned for test.");

        Assert.Equal("Pinned for test.", service.CurrentPrompt);
        Assert.True(service.IsLoaded);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Presets_DefaultClearsOverride()
    {
        var preset = Assert.Single(SystemPromptService.Presets, p => p.Name == "Default");
        Assert.Null(preset.Prompt);
        Assert.True(preset.ClearsOverride);
    }

    [Fact]
    public void Presets_NonDefault_DoNotClearOverride()
    {
        var concise = Assert.Single(SystemPromptService.Presets, p => p.Name == "Concise");
        Assert.False(concise.ClearsOverride);
        Assert.False(string.IsNullOrEmpty(concise.Prompt));
    }

    private (SystemPromptService Service, TestSupport.ProtectedJSRuntime Store) BuildService(
        Dictionary<string, string>? seed = null)
    {
        var store = _fixture.CreateRuntime(seed is null ? null : new Dictionary<string, string>(seed));
        var storage = _fixture.CreateStorage(store);
        return (new SystemPromptService(storage, NullLogger<SystemPromptService>.Instance), store);
    }
}
