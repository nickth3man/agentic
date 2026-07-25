using System.Security.Cryptography;
using System.Text.Json;
using Agentic.Chat.Services;
using Microsoft.JSInterop;

namespace Agentic.Chat.Tests;

public class ProtectedStorageHelpersTests
{
    [Fact]
    public void IsBestEffortPersistenceFailure_ClassifiesKnownAndUnknown()
    {
        Assert.True(ProtectedStorageHelpers.IsBestEffortPersistenceFailure(new InvalidOperationException()));
        Assert.True(ProtectedStorageHelpers.IsBestEffortPersistenceFailure(new CryptographicException()));
        Assert.True(ProtectedStorageHelpers.IsBestEffortPersistenceFailure(new JsonException()));
        Assert.True(ProtectedStorageHelpers.IsBestEffortPersistenceFailure(new JSException("js")));
        Assert.False(ProtectedStorageHelpers.IsBestEffortPersistenceFailure(new InvalidDataException("other")));
    }
}
