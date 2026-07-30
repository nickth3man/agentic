using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;

namespace Agentic.Chat.Tests.Fixtures;

/// <summary>
/// Shared xUnit fixture for tests that exercise Blazor's ProtectedBrowserStorage
/// backed by a fake IJSRuntime. Centralizes the runtime fakes, identity/faulty data
/// protectors, and storage construction helpers that were previously duplicated
/// across SelectedModelServiceTests, ChatSettingsServiceTests, SystemPromptServiceTests,
/// and ModelPickerPreferencesServiceTests.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance methods preserve the xUnit IClassFixture usage pattern.")]
public sealed class ProtectedBrowserStorageFixture
{
    public TestSupport.ProtectedJSRuntime CreateRuntime(Dictionary<string, string>? seed = null)
        => TestSupport.NewProtectedJSRuntime(seed is null ? null : new Dictionary<string, string>(seed));

    public ProtectedLocalStorage CreateStorage(
        IJSRuntime runtime,
        IDataProtectionProvider? dataProtection = null)
        => new(runtime, dataProtection ?? new EphemeralDataProtectionProvider());

    public ProtectedLocalStorage CreateStorage(
        Dictionary<string, string>? seed = null,
        IDataProtectionProvider? dataProtection = null)
    {
        var runtime = CreateRuntime(seed);
        return CreateStorage(runtime, dataProtection);
    }

    public NoInteropRuntime CreateNoInteropRuntime() => new();

    public ThrowingRuntime CreateThrowingRuntime(Exception error) => new(error);

    public IdentityDataProtectionProvider CreateIdentityProtector() => new();

    public FaultyDataProtectionProvider CreateFaultyProtector(Exception error) => new(error);

    /// <summary>
    /// Simulates the "browser is not interactive yet" state. The framework's
    /// localStorage.* interop calls go through ProtectedBrowserStorage.GetProtectedJsonAsync,
    /// which awaits this IJSRuntime; throwing InvalidOperationException here makes it
    /// surface as the same exception type ProtectedLocalStorage documentation warns about.
    /// </summary>
    public sealed class NoInteropRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("JS interop is not available in this test.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new InvalidOperationException("JS interop is not available in this test.");
    }

    public sealed class ThrowingRuntime(Exception error) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw error;

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw error;
    }

    /// <summary>
    /// Identity data protector — passes bytes through unchanged on both directions.
    /// Combined with the IDataProtector extension methods' base64 wrapping, this lets
    /// tests exercise specific decode-time failure modes by hand-crafting the stored
    /// base64 payload directly.
    /// </summary>
    public sealed class IdentityDataProtectionProvider : IDataProtectionProvider
    {
        private static readonly IDataProtector Protector = new IdentityDataProtector();

        public IDataProtector CreateProtector(string purpose) => Protector;

        private sealed class IdentityDataProtector : IDataProtector
        {
            public byte[] Protect(byte[] userData) => userData;
            public byte[] Unprotect(byte[] protectedData) => protectedData;
            public IDataProtector CreateProtector(string purpose) => this;
        }
    }

    /// <summary>
    /// Faulty data protector — throws the configured exception on Protect and Unprotect.
    /// Used to exercise the service-layer catch blocks (CryptographicException,
    /// JsonException) without depending on edge-case behavior of the real provider.
    /// </summary>
    public sealed class FaultyDataProtectionProvider : IDataProtectionProvider
    {
        private readonly Exception _onOp;

        public FaultyDataProtectionProvider(Exception onOp)
        {
            ArgumentNullException.ThrowIfNull(onOp);
            _onOp = onOp;
        }

        public IDataProtector CreateProtector(string purpose) => new FaultyDataProtector(_onOp);

        private sealed class FaultyDataProtector : IDataProtector
        {
            private readonly Exception _onOp;

            public FaultyDataProtector(Exception onOp)
            {
                ArgumentNullException.ThrowIfNull(onOp);
                _onOp = onOp;
            }

            public byte[] Protect(byte[] userData) => throw _onOp;
            public byte[] Unprotect(byte[] protectedData) => throw _onOp;
            public IDataProtector CreateProtector(string purpose) => this;
        }
    }
}
