using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentic.Chat.Tests.Fixtures;

/// <summary>
/// Shared xUnit fixture for tests that exercise HTTP-bound services.
/// Centralizes the stub handlers and throw-away <see cref="IHttpClientFactory"/>
/// that were previously duplicated across OpenRouterClientTests,
/// ModelCatalogServiceTests, MultiAgentProviderTests, and ChatAgentService tests.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance methods preserve the xUnit IClassFixture usage pattern.")]
public sealed class HttpMessageHandlerFixture
{
    public TextStubHandler CreateTextHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(body, status);

    public FuncStubHandler CreateFuncHandler(
        Func<HttpRequestMessage, CancellationToken, Task<(string Body, HttpStatusCode Status)>> respond)
        => new(respond);

    public MessageStubHandler CreateMessageHandler(HttpResponseMessage response)
        => new(response);

    public ThrowingHandler CreateThrowingHandler(string message = "Simulated network error")
        => new(message);

    public UnusedHttpClientFactory CreateUnusedFactory()
        => new();

    /// <summary>
    /// Returns a string body and status for <see cref="FuncStubHandler"/>.
    /// </summary>
    public static Task<(string Body, HttpStatusCode Status)> RespondJson(
        string body,
        HttpStatusCode status = HttpStatusCode.OK)
        => Task.FromResult((body, status));

    /// <summary>
    /// Stub handler that returns a fixed text/event-stream or JSON body.
    /// Captures the last request method, URI, and body for assertion.
    /// </summary>
    public sealed class TextStubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public TextStubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public HttpMethod? CapturedMethod { get; private set; }
        public Uri? CapturedUri { get; private set; }
        public string? CapturedBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedMethod = request.Method;
            CapturedUri = request.RequestUri;
            CapturedBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/event-stream")
            });
        }
    }

    /// <summary>
    /// Stub handler that delegates response generation to a function.
    /// Tracks the number of invocations and exposes a task that completes on first call.
    /// </summary>
    public sealed class FuncStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<(string Body, HttpStatusCode Status)>> _respond;
        private int _callCount;

        public FuncStubHandler(
            Func<HttpRequestMessage, CancellationToken, Task<(string Body, HttpStatusCode Status)>> respond)
        {
            _respond = respond;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public TaskCompletionSource EnteredTask { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _callCount);
            if (n == 1) EnteredTask.TrySetResult();
            var (body, status) = await _respond(request, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>
    /// Stub handler that returns a fixed <see cref="HttpResponseMessage"/>.
    /// </summary>
    public sealed class MessageStubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public MessageStubHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    /// <summary>
    /// Handler that always throws, simulating a network-level failure.
    /// </summary>
    public sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly string _message;

        public ThrowingHandler(string message = "Simulated network error") => _message = message;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException(_message);
    }

    /// <summary>
    /// Factory that throws if ever invoked. Used when a service is handed to a
    /// component that must not issue HTTP requests during the test (e.g. a
    /// pre-seeded <see cref="ModelCatalogService"/>).
    /// </summary>
    public sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("The HTTP client factory must not be used in this test.");
    }
}
