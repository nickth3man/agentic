using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Services;
using Agentic.Chat.Tests.Fixtures;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;

namespace Agentic.Chat.Tests;

// Storage-layer tests. Approach (no new PackageReference):
//   - Use the real ProtectedLocalStorage (sealed; public ctor accepts IJSRuntime and
//     IDataProtectionProvider) but back it with an IJSRuntime fake that speaks the
//     framework's localStorage interop protocol — same identifiers that
//     ProtectedBrowserStorage.GetProtectedJsonAsync / SetProtectedJsonAsync invoke:
//     "localStorage.getItem" / "setItem" / "removeItem" (see TestSupport).
//   - The fake returns the in-memory dict value; a per-test data-protector passes
//     payload bytes through unchanged so the only transformation on the round-trip
//     is base64 (a side effect of the IDataProtector extension methods).
//   - The coverage gate (cover each catch: InvalidOperationException,
//     CryptographicException, JsonException) is satisfied by:
//       * InvalidOperationException — NoInteropRuntime throws synchronously on
//         every JSRuntime call, mirroring the "browser is not interactive yet"
//         state in the prerender path.
//       * CryptographicException — EphemeralDataProtectionProvider + a payload that
//         was protected by a different key (AAD mismatch).
//       * JsonException — payload is valid base64 but its cleartext is not a valid
//         JSON string token.
public class SelectedModelServiceTests : IClassFixture<ProtectedBrowserStorageFixture>
{
    private readonly ProtectedBrowserStorageFixture _fixture;

    public SelectedModelServiceTests(ProtectedBrowserStorageFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoadAsync_WithNoStoredValue_SetsIsLoadedTrue_CurrentModelIdNull()
    {
        var (service, store) = BuildService(seed: null);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentModelId);
        Assert.Empty(store.Store);
        Assert.Equal(1, changes); // LoadAsync fires OnChange exactly once on the finally branch
    }

    [Fact]
    public async Task LoadAsync_WithStoredValue_SetsCurrentModelId_RaisesOnChange()
    {
        // Use a shared data protection provider so the writer's protected payload can
        // be decrypted by the reader — the test exercises the storage round-trip path,
        // not the cryptographic mismatch path (covered separately below).
        var js = _fixture.CreateRuntime();
        var sharedDp = new EphemeralDataProtectionProvider();
        var initial = new SelectedModelService(_fixture.CreateStorage(js, sharedDp));
        await initial.SetAsync("openai/gpt-4o");
        Assert.True(js.Store.ContainsKey(SelectedModelService.StorageKey));

        var fresh = new SelectedModelService(_fixture.CreateStorage(js, sharedDp));
        var changes = 0;
        fresh.OnChange += () => changes++;

        await fresh.LoadAsync();

        Assert.True(fresh.IsLoaded);
        Assert.Equal("openai/gpt-4o", fresh.CurrentModelId);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task LoadAsync_OnNoJsInterop_SwallowsInvalidOperationException_IsLoadedTrue()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateNoInteropRuntime(),
            new EphemeralDataProtectionProvider());
        var service = new SelectedModelService(storage);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentModelId); // treated as no stored value
    }

    [Fact]
    public async Task LoadAsync_OnCorruptedStorage_SwallowsJsonException_IsLoadedTrue()
    {
        // Pipeline we trigger:
        //   1. GetProtectedJsonAsync returns the base64 we seeded below.
        //   2. Extension Unprotect(base64) decodes and calls IDataProtector.Unprotect(bytes),
        //      which is identity here, so we get back the UTF-8 cleartext.
        //   3. The cleartext is "not a JSON string" (unquoted). JsonSerializer.Deserialize<string>
        //      throws JsonException because the input isn't a valid JSON string token.
        //   4. SelectedModelService.LoadAsync's catch(JsonException) absorbs it.
        var seedBytes = Encoding.UTF8.GetBytes("not a JSON string");
        var seedProtected = Convert.ToBase64String(seedBytes);
        var js = _fixture.CreateRuntime(new Dictionary<string, string>
        {
            [SelectedModelService.StorageKey] = seedProtected
        });

        var storage = _fixture.CreateStorage(js, _fixture.CreateIdentityProtector());
        var service = new SelectedModelService(storage);

        await service.LoadAsync();

        Assert.True(service.IsLoaded);
        Assert.Null(service.CurrentModelId);
    }

    [Fact]
    public async Task LoadAsync_OnWrongProtectedProvider_SwallowsCryptographicException_IsLoadedTrue()
    {
        // Pipeline we trigger:
        //   1. Payload was protected by EphemeralDataProtectionProvider "A".
        //   2. We read it back with a brand-new EphemeralDataProtectionProvider "B"
        //      (different master keys, therefore different sub-keys), so
        //      IDataProtector.Unprotect throws CryptographicException due to the AAD
        //      mismatch on the protected payload's MAC.
        //   3. SelectedModelService.LoadAsync's catch(CryptographicException) absorbs it.
        var js = _fixture.CreateRuntime();
        var writer = new SelectedModelService(_fixture.CreateStorage(js));
        await writer.SetAsync("openai/gpt-4o");
        Assert.True(js.Store.ContainsKey(SelectedModelService.StorageKey));

        var reader = new SelectedModelService(_fixture.CreateStorage(js));
        var before = reader.CurrentModelId;

        await reader.LoadAsync();

        Assert.True(reader.IsLoaded);
        Assert.Equal(before, reader.CurrentModelId); // unchanged — treated as no stored value
    }

    [Fact]
    public async Task SetAsync_PersistsValue_UpdatesCurrentModelId_RaisesOnChange()
    {
        var (service, store) = BuildService(seed: null);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.SetAsync("openai/gpt-oss-120b");

        Assert.True(service.IsLoaded);
        Assert.Equal("openai/gpt-oss-120b", service.CurrentModelId);
        Assert.True(store.Store.ContainsKey(SelectedModelService.StorageKey));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task SetAsync_OnStorageFailure_StillUpdatesInMemory_RaisesOnChange()
    {
        var storage = _fixture.CreateStorage(
            _fixture.CreateNoInteropRuntime(),
            new EphemeralDataProtectionProvider());
        var service = new SelectedModelService(storage);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.SetAsync("anthropic/claude-3.5-sonnet");

        Assert.True(service.IsLoaded);
        Assert.Equal("anthropic/claude-3.5-sonnet", service.CurrentModelId);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task SetAsync_NullOrEmptyId_Throws()
    {
        var (service, _) = BuildService(seed: null);
        // ThrowIfNullOrEmpty throws ArgumentNullException for null and ArgumentException
        // for empty — both derive from ArgumentException, so use ThrowsAnyAsync.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SetAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(string.Empty));
    }

    [Fact]
    public async Task SetAsync_OnCryptographicExceptionOnProtect_Swallows_StillUpdatesInMemory()
    {
        // The framework's Protect -> IDataProtector.Protect(byte[]) can throw
        // CryptographicException (e.g. when the data-protection subsystem is in a
        // bad state). SelectedModelService.SetAsync must catch and proceed.
        var js = _fixture.CreateRuntime();
        var faultyDp = _fixture.CreateFaultyProtector(new CryptographicException("synthetic"));
        var storage = _fixture.CreateStorage(js, faultyDp);
        var service = new SelectedModelService(storage);
        var changes = 0;
        service.OnChange += () => changes++;

        await service.SetAsync("openai/gpt-4o");

        Assert.True(service.IsLoaded);
        Assert.Equal("openai/gpt-4o", service.CurrentModelId);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task SetAsync_OnJsonExceptionFromProtect_Swallows_StillUpdatesInMemory()
    {
        // JsonException is unlikely from the real Protect path on a string value,
        // but a malformed IDataProtector implementation could surface it. Verify
        // SetAsync swallows it the same way LoadAsync does.
        var js = _fixture.CreateRuntime();
        var faultyDp = _fixture.CreateFaultyProtector(new JsonException("synthetic"));
        var storage = _fixture.CreateStorage(js, faultyDp);
        var service = new SelectedModelService(storage);

        await service.SetAsync("openai/gpt-4o");

        Assert.True(service.IsLoaded);
        Assert.Equal("openai/gpt-4o", service.CurrentModelId);
    }

    [Fact]
    public void SetCurrentModelIdForTest_UpdatesStateAndRaisesOnChange()
    {
        var js = _fixture.CreateRuntime();
        var service = new SelectedModelService(_fixture.CreateStorage(js));
        var changes = 0;
        service.OnChange += () => changes++;

        service.SetCurrentModelIdForTest("anthropic/claude-3.5-sonnet");

        Assert.Equal("anthropic/claude-3.5-sonnet", service.CurrentModelId);
        Assert.True(service.IsLoaded);
        Assert.Equal(1, changes);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private (SelectedModelService Service, TestSupport.ProtectedJSRuntime Store) BuildService(
        Dictionary<string, string>? seed = null)
    {
        var store = _fixture.CreateRuntime(seed is null ? null : new Dictionary<string, string>(seed));
        var storage = _fixture.CreateStorage(store);
        return (new SelectedModelService(storage), store);
    }
}
