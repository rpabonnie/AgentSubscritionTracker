using System.Net;
using System.Net.Http;
using System.Text;

namespace AgentSubscriptionTracker.Tests.TestSupport;

/// <summary>
/// Scripted <see cref="HttpMessageHandler"/> for network-free tests (CLAUDE.md: no live
/// network calls). Responses are dequeued in FIFO order; every request (method, URI,
/// headers, body) is captured for assertions. Throws if a request arrives with no
/// scripted response, so tests fail loudly on unexpected traffic.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
    private readonly List<CapturedRequest> _requests = [];

    /// <summary>All requests seen by the handler, in order.</summary>
    public IReadOnlyList<CapturedRequest> Requests => _requests;

    /// <summary>Queue a fixed-status response with a string body and optional response headers.</summary>
    public void Enqueue(HttpStatusCode statusCode, string body, IDictionary<string, string>? headers = null)
    {
        _responders.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (headers is not null)
            {
                foreach (var pair in headers)
                {
                    response.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                }
            }

            return response;
        });
    }

    /// <summary>Queue a responder that inspects the request before answering.</summary>
    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _responders.Enqueue(responder);
    }

    /// <summary>Queue an exception (e.g. <see cref="HttpRequestException"/>) for the next request.</summary>
    public void EnqueueException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _responders.Enqueue(_ => throw exception);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        _requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            request.Headers.UserAgent.ToString(),
            request.Headers.TryGetValues("anthropic-beta", out var beta) ? string.Join(",", beta) : null,
            body));

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeHttpMessageHandler: unexpected request {request.Method} {request.RequestUri} — no scripted response left.");
        }

        return _responders.Dequeue()(request);
    }
}

/// <summary>Immutable capture of one outbound request, safe to inspect after disposal.</summary>
public sealed record CapturedRequest(
    HttpMethod Method,
    Uri? Uri,
    string? AuthorizationScheme,
    string? AuthorizationParameter,
    string UserAgent,
    string? AnthropicBeta,
    string? Body);
