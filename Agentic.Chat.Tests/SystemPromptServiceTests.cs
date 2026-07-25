using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;

namespace Agentic.Chat.Tests;

// Storage-layer tests for SystemPromptService. Mirrors SelectedModelServiceTests:
// real ProtectedLocalStorage + TestSupport.ProtectedJSRuntime, with dedicated
// IDataProtectionProvider fakes to hit each LoadAsync/SetAsync catch branch.
public class SystemPromptServiceTests
{
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
        var js = TestSupport.NewProtectedJSRuntime();
        var sharedDp = new EphemeralDataProtectionProvider();
        var initial = new SystemPromptService(BuildStorage(js, sharedDp));
        await initial.SetAsync("Stored system prompt.");
        Assert.True(js.Store.ContainsKey(SystemPromptService.StorageKey));

        var fresh = new SystemPromptService(BuildStorage(js, sharedDp));
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
        var storage = new ProtectedLocalStorage(new NoInteropJSRuntime(), new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentPrompt);
    }

    [Fact]
    public async Task LoadAsync_OnCorruptedStorage_SwallowsJsonException_IsLoadedTrue()
    {
        var seedBytes = Encoding.UTF8.GetBytes("not a JSON string");
        var seedProtected = Convert.ToBase64String(seedBytes);
        var js = TestSupport.NewProtectedJSRuntime(new Dictionary<string, string>
        {
            [SystemPromptService.StorageKey] = seedProtected
        });

        var storage = new ProtectedLocalStorage(js, new IdentityDataProtectionProvider());
        var service = new SystemPromptService(storage);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentPrompt);
    }

    [Fact]
    public async Task LoadAsync_OnWrongProtectedProvider_SwallowsCryptographicException_IsLoadedTrue()
    {
        var js = TestSupport.NewProtectedJSRuntime();
        var writer = new SystemPromptService(new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider()));
        await writer.SetAsync("A prompt.");
        Assert.True(js.Store.ContainsKey(SystemPromptService.StorageKey));

        var reader = new SystemPromptService(new ProtectedLocalStorage(js, new EphemeralDataProtectionProvider()));
        var before = reader.CurrentPrompt;

        await reader.LoadAsync();

        Assert.True(reader.IsLoaded);
        Assert.Equal(before, reader.CurrentPrompt);
    }

    [Fact]
    public async Task LoadAsync_WithStoredEmptyString_LeavesCurrentPromptNull()
    {
        // Success=true with empty Value must not assign CurrentPrompt
        // (!string.IsNullOrEmpty branch of the LoadAsync guard).
        var seedBytes = Encoding.UTF8.GetBytes("\"\"");
        var seedProtected = Convert.ToBase64String(seedBytes);
        var js = TestSupport.NewProtectedJSRuntime(new Dictionary<string, string>
        {
            [SystemPromptService.StorageKey] = seedProtected
        });

        var service = new SystemPromptService(new ProtectedLocalStorage(js, new IdentityDataProtectionProvider()));

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
        var storage = new ProtectedLocalStorage(new NoInteropJSRuntime(), new EphemeralDataProtectionProvider());
        var service = new SystemPromptService(storage);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.SetAsync("In-memory only prompt.");

        Assert.True(service.IsLoaded);
        Assert.Equal("In-memory only prompt.", service.CurrentPrompt);
        Assert.Equal(1, changes);
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
    public async Task SetAsync_OnCryptographicExceptionOnProtect_Swallows_StillUpdatesInMemory()
    {
        var js = TestSupport.NewProtectedJSRuntime();
        var faultyDp = new FaultyDataProtectionProvider(new CryptographicException("synthetic"));
        var storage = new ProtectedLocalStorage(js, faultyDp);
        var service = new SystemPromptService(storage);
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
        var js = TestSupport.NewProtectedJSRuntime();
        var faultyDp = new FaultyDataProtectionProvider(new JsonException("synthetic"));
        var storage = new ProtectedLocalStorage(js, faultyDp);
        var service = new SystemPromptService(storage);

        await service.SetAsync("Prompt despite json failure.");

        Assert.True(service.IsLoaded);
        Assert.Equal("Prompt despite json failure.", service.CurrentPrompt);
    }

    [Fact]
    public void SetCurrentPromptForTest_UpdatesStateAndRaisesOnChange()
    {
        var js = TestSupport.NewProtectedJSRuntime();
        var service = new SystemPromptService(BuildStorage(js));
        var changes = 0;
        service.OnChange += () => changes++;

        service.SetCurrentPromptForTest("Pinned for test.");

        Assert.Equal("Pinned for test.", service.CurrentPrompt);
        Assert.True(service.IsLoaded);
        Assert.Equal(1, changes);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (SystemPromptService Service, TestSupport.ProtectedJSRuntime Store) BuildService(
        Dictionary<string, string>? seed = null)
    {
        var store = TestSupport.NewProtectedJSRuntime(seed is null ? null : new Dictionary<string, string>(seed));
        var storage = BuildStorage(store);
        return (new SystemPromptService(storage), store);
    }

    private static ProtectedLocalStorage BuildStorage(TestSupport.ProtectedJSRuntime store, IDataProtectionProvider? dp = null)
        => new(store, dp ?? new EphemeralDataProtectionProvider());

    private sealed class NoInteropJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("JS interop is not available in this test.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw new InvalidOperationException("JS interop is not available in this test.");
    }

    private sealed class IdentityDataProtectionProvider : IDataProtectionProvider
    {
        private static readonly IDataProtector Protector = new IdentityDataProtector();

        public IDataProtector CreateProtector(string purpose) => Protector;
    }

    private sealed class IdentityDataProtector : IDataProtector
    {
        public byte[] Protect(byte[] userData) => userData;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
        public IDataProtector CreateProtector(string purpose) => this;
    }

    private sealed class FaultyDataProtectionProvider : IDataProtectionProvider
    {
        private readonly Exception _onOp;
        public FaultyDataProtectionProvider(Exception onOp) => _onOp = onOp;
        public IDataProtector CreateProtector(string purpose) => new FaultyDataProtector(_onOp);
    }

    private sealed class FaultyDataProtector : IDataProtector
    {
        private readonly Exception _onOp;
        public FaultyDataProtector(Exception onOp) => _onOp = onOp;
        public byte[] Protect(byte[] userData) => throw _onOp;
        public byte[] Unprotect(byte[] protectedData) => throw _onOp;
        public IDataProtector CreateProtector(string purpose) => this;
    }
}
