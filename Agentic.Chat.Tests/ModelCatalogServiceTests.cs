using System.Net;
using System.Text;
using System.Text.Json;
using Agentic.Chat.Models;
using Agentic.Chat.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Chat.Tests;

public class ModelCatalogServiceTests
{
    private const string RepresentativeJson = """
        {
          "id": "openai/gpt-4o",
          "name": "GPT-4o",
          "context_length": 128000,
          "created": 1722384000,
          "architecture": { "modality": "text->text" },
          "pricing": { "prompt": "0.0000025", "completion": "0.00001" },
          "supported_parameters": ["tools", "reasoning", "tool_choice"]
        }
        """;

    private static string Envelope(params string[] modelsJson)
    {
        // Even for empty `data` arrays STJ needs a syntactically valid envelope.
        var sb = new StringBuilder();
        sb.Append("{\"data\":[");
        for (int i = 0; i < modelsJson.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(modelsJson[i]);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static ModelCatalogService BuildService(StubHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("OpenRouter", client =>
            {
                client.BaseAddress = new Uri("https://test.local/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new ModelCatalogService(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }

    [Fact]
    public async Task GetModelsAsync_OnFirstCall_FetchesAndCaches()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK)));
        var service = BuildService(handler);

        var list = await service.GetModelsAsync();

        Assert.Equal(1, handler.CallCount);
        Assert.Single(list);
        Assert.Equal("openai/gpt-4o", list[0].Id);
        Assert.Equal("openai", list[0].Provider);
    }

    [Fact]
    public async Task GetModelsAsync_OnSecondCallWithinTtl_DoesNotRefetch()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK)));
        var service = BuildService(handler);

        _ = await service.GetModelsAsync();
        _ = await service.GetModelsAsync();

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetModelsAsync_AfterTtlExpires_Refetches()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK)));
        var service = BuildService(handler);

        _ = await service.GetModelsAsync();
        Assert.Equal(1, handler.CallCount);

        // Test seam: backs _cachedAt up by a span larger than CacheDuration so the
        // next call observes a stale cache and refetches.
        service.FastForwardCache(TimeSpan.FromMinutes(20));
        _ = await service.GetModelsAsync();

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RefreshAsync_AlwaysFetches_RegardlessOfCache()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK)));
        var service = BuildService(handler);

        _ = await service.GetModelsAsync();
        Assert.Equal(1, handler.CallCount);

        await service.RefreshAsync();
        _ = await service.GetModelsAsync();

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetModelsAsync_OnFetchFailureWithNoCache_Throws()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((string.Empty, HttpStatusCode.InternalServerError)));
        var service = BuildService(handler);

        // 500 surfaces from GetStreamAsync's EnsureSuccessStatusCode as HttpRequestException.
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetModelsAsync());
    }

    [Fact]
    public async Task GetModelsAsync_OnFetchFailureWithExistingCache_ReturnsStale_NoThrow()
    {
        int calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            var n = Interlocked.Increment(ref calls);
            // First call: success. Subsequent calls: 500.
            if (n == 1)
            {
                return Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK));
            }
            return Task.FromResult((string.Empty, HttpStatusCode.InternalServerError));
        });
        var service = BuildService(handler);

        var first = await service.GetModelsAsync();
        Assert.Single(first);
        Assert.Equal(1, handler.CallCount);

        // Stale the cache so the next call would normally refetch.
        service.FastForwardCache(TimeSpan.FromMinutes(20));

        var second = await service.GetModelsAsync();
        Assert.Same(first, second); // same cached instance, stale returned silently
        Assert.Equal(2, handler.CallCount); // second handler invocation attempted, then fallback
    }

    [Fact]
    public async Task GetModelsAsync_OnEmptyDataArray_ReturnsEmptyList()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(("{\"data\":[]}", HttpStatusCode.OK)));
        var service = BuildService(handler);

        var list = await service.GetModelsAsync();

        Assert.NotNull(list);
        Assert.Empty(list);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FindByIdAsync_Hit_ReturnsModel()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK)));
        var service = BuildService(handler);

        var model = await service.FindByIdAsync("openai/gpt-4o");

        Assert.NotNull(model);
        Assert.Equal("GPT-4o", model!.Name);
    }

    [Fact]
    public async Task FindByIdAsync_Miss_ReturnsNull()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK)));
        var service = BuildService(handler);

        var model = await service.FindByIdAsync("not/in/catalog");

        Assert.Null(model);
    }

    [Fact]
    public async Task FindByIdAsync_TrimsAndMatchesIdCaseInsensitively()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK)));
        var service = BuildService(handler);

        var model = await service.FindByIdAsync("  OpenAI/GPT-4o  ");

        Assert.NotNull(model);
        Assert.Equal("openai/gpt-4o", model!.Id);
    }

    [Fact]
    public async Task FindByIdAsync_NullOrEmptyId_Throws()
    {
        var handler = new StubHandler((_, _) => Task.FromResult((Envelope(RepresentativeJson), HttpStatusCode.OK)));
        var service = BuildService(handler);

        // ThrowIfNullOrEmpty throws ArgumentNullException for null and ArgumentException
        // for empty — both derive from ArgumentException, so use ThrowsAnyAsync.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.FindByIdAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.FindByIdAsync(string.Empty));
    }

    [Fact]
    public void ParseModelInternal_ParsesAllFieldsCorrectly()
    {
        using var doc = JsonDocument.Parse(RepresentativeJson);

        var model = ModelCatalogService.ParseModel(doc.RootElement);

        Assert.Equal("openai/gpt-4o", model.Id);
        Assert.Equal("GPT-4o", model.Name);
        Assert.Equal(128_000L, model.ContextLength);
        Assert.Equal("text->text", model.Modality);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_722_384_000), model.Created);
        Assert.Equal(0.0000025m, model.Pricing.PromptPerToken);
        Assert.Equal(0.00001m, model.Pricing.CompletionPerToken);
        Assert.Equal(new[] { "tools", "reasoning", "tool_choice" }, model.SupportedParameters);
        Assert.True(model.SupportsReasoning);
        Assert.Equal("openai", model.Provider);
    }

    [Fact]
    public void ParseModelInternal_ToleratesNullSubobjects()
    {
        var json = """
            {
              "id": "x/y",
              "name": "Y",
              "context_length": 0,
              "created": 0,
              "architecture": null,
              "pricing": null,
              "supported_parameters": null
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var model = ModelCatalogService.ParseModel(doc.RootElement);

        Assert.Equal(string.Empty, model.Modality);
        Assert.Equal(0m, model.Pricing.PromptPerToken);
        Assert.Equal(0m, model.Pricing.CompletionPerToken);
        Assert.Empty(model.SupportedParameters);
        Assert.True(model.IsFree); // zero pricing → free fallback
    }

    [Fact]
    public async Task GetModelsAsync_ConcurrentCalls_DeduplicatesToSingleHttpRequest()
    {
        // Force the critical-section overlap so two callers race the SemaphoreSlim.
        var gate = new TaskCompletionSource();
        int calls = 0;
        var handler = new StubHandler(async (_, ct) =>
        {
            var n = Interlocked.Increment(ref calls);
            if (n == 1)
            {
                await gate.Task.WaitAsync(ct);
            }
            return (Envelope(RepresentativeJson), HttpStatusCode.OK);
        });
        var service = BuildService(handler);

        var taskA = service.GetModelsAsync();
        // Wait until the handler saw request #1, so we know taskA is in flight.
        await handler.EnteredTask.Task;
        var taskB = service.GetModelsAsync();

        gate.SetResult();
        var listA = await taskA;
        var listB = await taskB;

        Assert.Equal(1, handler.CallCount);
        Assert.Single(listA);
        Assert.Single(listB);
    }

    [Fact]
    public async Task GetModelsAsync_OnCancellationDuringFetch_PropagatesOperationCanceledException()
    {
        // Cancellation between WaitAsync-entered and FetchAsync-completed exercises
        // the explicit catch(OperationCanceledException) { throw; } branch that
        // distinguishes user-cancellation from generic network failure (which falls
        // back to the stale cache).
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return (Envelope(RepresentativeJson), HttpStatusCode.OK);
        });
        var service = BuildService(handler);

        using var cts = new CancellationTokenSource();
        var task = service.GetModelsAsync(cts.Token);

        await handler.EnteredTask.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public void ParseModelInternal_PricingPresentButFieldsAreNull_CoversBothNullCoalesceBranches()
    {
        // The pricing object is present (so the ?. short-circuit doesn't fire) but
        // its scalar fields are explicit JSON nulls — exercising the inner ??
        // branch of `raw.Pricing?.Prompt ?? "0"` and the matching one for completion.
        var json = """
            {
              "id": "x/y",
              "name": "Y",
              "context_length": 128000,
              "created": 1700000000,
              "architecture": { "modality": "text+text->text+text" },
              "pricing": { "prompt": null, "completion": null },
              "supported_parameters": []
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var model = ModelCatalogService.ParseModel(doc.RootElement);

        Assert.Equal(0m, model.Pricing.PromptPerToken);
        Assert.Equal(0m, model.Pricing.CompletionPerToken);
        Assert.Equal("text+text->text+text", model.Modality);
    }

    [Fact]
    public void ParseModelInternal_OptionalScalarsAsNull_CoverAllNullCoalesceBranches()
    {
        // Every nullable string field set to JSON null: id=null, name=null,
        // architecture.modality=null. Each ?? operator's "left is null, use "" "
        // branch fires AFTER the ?. short-circuit, exercising the three ?? branches
        // for Id, Name, and Architecture.Modality.
        var json = """
            {
              "id": null,
              "name": null,
              "context_length": 0,
              "created": 0,
              "architecture": { "modality": null },
              "pricing": null,
              "supported_parameters": null
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var model = ModelCatalogService.ParseModel(doc.RootElement);

        Assert.Equal(string.Empty, model.Id);
        Assert.Equal(string.Empty, model.Name);
        Assert.Equal(string.Empty, model.Modality);
    }

    [Fact]
    public void ParseModelInternal_NullJsonElement_Throws()
    {
        // Triggers the `?? throw new InvalidOperationException(...)` branch on the
        // element.Deserialize<RawModel>(...) call.
        using var doc = JsonDocument.Parse("null");

        Assert.Throws<InvalidOperationException>(
            () => ModelCatalogService.ParseModel(doc.RootElement));
    }

    [Fact]
    public async Task GetModelsAsync_OnEnvelopeWithoutData_ReturnsEmptyList()
    {
        // {} envelope — STJ produces an envelope with Data=null. FetchAsync's
        // `envelope?.Data is null || ...` short-circuits to true; the
        // IsNull branch gets exercised.
        var handler = new StubHandler((_, _) => Task.FromResult(("{}", HttpStatusCode.OK)));
        var service = BuildService(handler);

        var list = await service.GetModelsAsync();

        Assert.Empty(list);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetModelsAsync_OnNullDataField_ReturnsEmptyList()
    {
        // Explicit "data": null — exercises the `envelope.Data is null` branch
        // (after the `?.` short-circuit evaluated envelope).
        var handler = new StubHandler((_, _) => Task.FromResult(("{\"data\":null}", HttpStatusCode.OK)));
        var service = BuildService(handler);

        var list = await service.GetModelsAsync();

        Assert.Empty(list);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetModelsAsync_OnJsonNullResponse_ReturnsEmptyList()
    {
        // Top-level JSON null makes STJ's DeserializeAsync<ModelListEnvelope> return
        // a null envelope — exercises the `?.` short-circuit branch.
        var handler = new StubHandler((_, _) => Task.FromResult(("null", HttpStatusCode.OK)));
        var service = BuildService(handler);

        var list = await service.GetModelsAsync();

        Assert.Empty(list);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<(string Body, HttpStatusCode Status)>> _respond;
        private int _callCount;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<(string Body, HttpStatusCode Status)>> respond)
        {
            _respond = respond;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public TaskCompletionSource EnteredTask { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _callCount);
            if (n == 1) EnteredTask.TrySetResult();
            var (body, status) = await _respond(request, cancellationToken).ConfigureAwait(false);
            var msg = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return msg;
        }
    }
}
