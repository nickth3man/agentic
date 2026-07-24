using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.JSInterop;

namespace Agentic.Chat.Tests;

// Test helpers shared across SelectedModelServiceTests and ChatAgentServiceSendStreamingTests.
// Real EphemeralDataProtectionProvider + a hand-rolled IJSRuntime fake that speaks the
// framework's localStorage interop protocol — the same one
// ProtectedBrowserStorage.GetProtectedJsonAsync / SetProtectedJsonAsync invoke.
internal static class TestSupport
{
    public static ProtectedJSRuntime NewProtectedJSRuntime(Dictionary<string, string>? seed = null)
        => new(seed);

    public sealed class ProtectedJSRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> _store;

        public ProtectedJSRuntime(Dictionary<string, string>? seed)
        {
            _store = seed is not null ? new Dictionary<string, string>(seed) : new();
        }

        public IReadOnlyDictionary<string, string> Store => _store;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (identifier)
            {
                case "localStorage.getItem":
                    {
                        var key = (string)args![0]!;
                        if (_store.TryGetValue(key, out var v))
                        {
                            return new ValueTask<TValue>(result: (TValue)(object)v);
                        }
                        return new ValueTask<TValue>(result: default!);
                    }
                case "localStorage.setItem":
                    {
                        var key = (string)args![0]!;
                        var value = (string)args![1]!;
                        _store[key] = value;
                        // IJSVoidResult is internal; the framework's InvokeVoidAsync wraps
                        // any TValue return into a completed ValueTask.
                        return new ValueTask<TValue>(result: default!);
                    }
                case "localStorage.removeItem":
                    {
                        var key = (string)args![0]!;
                        _store.Remove(key);
                        return new ValueTask<TValue>(result: default!);
                    }
                default:
                    throw new InvalidOperationException($"Unexpected JS interop call: {identifier}");
            }
        }
    }
}

internal sealed class FakeOpenRouterClient : IOpenRouterClient
{
    // Per-call scripted responses (FIFO). The single-response ctor enqueues a
    // single response; multi-call tests (retry/regenerate/multi-send) enqueue
    // one per expected call so a single service instance can answer differently
    // across send/retry/regenerate.
    private readonly Queue<FakeResponse> _responses = new();

    public ChatCompletionRequest? LastRequest { get; private set; }

    public int CallCount { get; private set; }

    public IReadOnlyList<FakeResponse> RemainingResponses => _responses.ToArray();

    public FakeOpenRouterClient(IEnumerable<StreamDelta>? deltas = null, Exception? exception = null)
    {
        _responses.Enqueue(new FakeResponse(deltas?.ToList() ?? [], exception));
    }

    public FakeOpenRouterClient(params FakeResponse[] responses)
    {
        foreach (var r in responses)
        {
            _responses.Enqueue(r);
        }
    }

    public async IAsyncEnumerable<StreamDelta> StreamChatAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Snapshot the transcript at the call boundary; ChatAgentService appends the
        // completed assistant message to its live list after the client returns.
        LastRequest = request with { Messages = request.Messages.ToList() };
        CallCount++;
        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : new FakeResponse([], null);
        if (response.Exception is not null)
        {
            throw response.Exception;
        }
        foreach (var delta in response.Deltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return delta;
        }
    }

    public sealed record FakeResponse(IReadOnlyList<StreamDelta> Deltas, Exception? Exception)
    {
        public static FakeResponse Ok(params StreamDelta[] deltas) => new(deltas, null);

        public static FakeResponse Err(Exception ex) => new([], ex);
    }
}
