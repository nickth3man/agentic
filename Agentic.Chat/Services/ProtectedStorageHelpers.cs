using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Agentic.Chat.Services;

// Shared filter for ProtectedLocalStorage failures that must stay best-effort
// (prerender, crypto, shape drift, JS interop) so in-memory state still wins.
internal static class ProtectedStorageHelpers
{
    internal static bool IsBestEffortPersistenceFailure(Exception ex)
        => ex is InvalidOperationException
            or CryptographicException
            or JsonException
            or JSException;
}
