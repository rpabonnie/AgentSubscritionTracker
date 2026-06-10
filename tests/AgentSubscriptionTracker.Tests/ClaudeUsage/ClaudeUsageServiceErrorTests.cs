using System.Net;
using System.Net.Http;
using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.Tests.TestSupport;

namespace AgentSubscriptionTracker.Tests.ClaudeUsage;

/// <summary>
/// SPEC-0001 §4.1 steps 3 & 5, §4.2, §5 — provider states, token refresh, 401/403/429
/// handling, transient retries, and redaction. GetUsageAsync never throws; every failure
/// is a typed state.
/// </summary>
public sealed class ClaudeUsageServiceErrorTests
{
    private static readonly Uri PrimaryRefreshEndpoint = new("https://platform.claude.com/v1/oauth/token");
    private static readonly Uri FallbackRefreshEndpoint = new("https://console.anthropic.com/v1/oauth/token");

    [Fact]
    public async Task GetUsageAsync_NoCredentials_ReturnsNotSignedInWithoutAnyHttpCall()
    {
        // Arrange — research doc: file absent => "Sign in via Claude Code" state.
        var handler = new FakeHttpMessageHandler(); // nothing scripted: any request throws
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader((ClaudeOAuthCredentials?)null);
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.NotSignedIn, snapshot.State);
        Assert.Empty(handler.Requests);
        Assert.Null(snapshot.FiveHour);
        Assert.Equal(ClaudeTestData.TestNow, snapshot.RetrievedAt);
    }

    [Fact]
    public async Task GetUsageAsync_ExpiredToken_RefreshSucceeds_UsesRefreshedTokenInMemory()
    {
        // Arrange — expiresAt < now: POST refresh first, then GET usage with the NEW token.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("refresh.success.json"));
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ExpiredCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Ok, snapshot.State);
        Assert.Equal(2, handler.Requests.Count);

        var refreshRequest = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, refreshRequest.Method);
        Assert.Equal(PrimaryRefreshEndpoint, refreshRequest.Uri);
        Assert.NotNull(refreshRequest.Body);
        Assert.Contains("refresh_token", refreshRequest.Body, StringComparison.Ordinal);
        Assert.Contains(ClaudeTestData.FakeRefreshToken, refreshRequest.Body, StringComparison.Ordinal);
        Assert.Contains(ClaudeTestData.OAuthClientId, refreshRequest.Body, StringComparison.Ordinal);

        var usageRequest = handler.Requests[1];
        Assert.Equal(HttpMethod.Get, usageRequest.Method);
        Assert.Equal(ClaudeTestData.FakeRefreshedAccessToken, usageRequest.AuthorizationParameter);
    }

    [Fact]
    public async Task GetUsageAsync_ExpiredToken_PrimaryRefreshEndpointFails_FallsBackToSecondEndpoint()
    {
        // Arrange — SPEC-0001 §4.2: platform.claude.com first, console.anthropic.com on failure.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("refresh.success.json"));
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ExpiredCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Ok, snapshot.State);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(PrimaryRefreshEndpoint, handler.Requests[0].Uri);
        Assert.Equal(FallbackRefreshEndpoint, handler.Requests[1].Uri);
        Assert.Equal(ClaudeTestData.FakeRefreshedAccessToken, handler.Requests[2].AuthorizationParameter);
    }

    [Fact]
    public async Task GetUsageAsync_ExpiredToken_AllRefreshEndpointsFail_ReturnsTokenExpired()
    {
        // Arrange — refresh failed on both endpoints => "re-login in Claude Code".
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        handler.Enqueue(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ExpiredCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.TokenExpired, snapshot.State);
        Assert.Equal(2, handler.Requests.Count); // no usage GET was attempted
    }

    [Fact]
    public async Task GetUsageAsync_ExpiredTokenWithoutRefreshToken_ReturnsTokenExpiredWithoutHttpCall()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ExpiredCredentials(withRefreshToken: false));
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.TokenExpired, snapshot.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetUsageAsync_Unauthorized_RefreshSucceeds_RetriesOnceAndReturnsOk()
    {
        // Arrange — 401 on a non-expired token: one refresh + one retried GET (SPEC-0001 §4.1.5).
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, ClaudeTestData.ReadFixture("error.401.json"));
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("refresh.success.json"));
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Ok, snapshot.State);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(ClaudeTestData.FakeAccessToken, handler.Requests[0].AuthorizationParameter);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal(ClaudeTestData.FakeRefreshedAccessToken, handler.Requests[2].AuthorizationParameter);
    }

    [Fact]
    public async Task GetUsageAsync_Unauthorized_RefreshFails_ReturnsTokenExpired()
    {
        // Arrange — 401 then refresh failure on both endpoints.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, ClaudeTestData.ReadFixture("error.401.json"));
        handler.Enqueue(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        handler.Enqueue(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.TokenExpired, snapshot.State);
    }

    [Fact]
    public async Task GetUsageAsync_Forbidden_ReturnsUnavailableWithoutRefreshAttempt()
    {
        // Arrange — 403: authenticated but not authorized; refresh would not help.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, """{ "type": "error", "error": { "type": "permission_error", "message": "Forbidden" } }""");
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Unavailable, snapshot.State);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetUsageAsync_RateLimited_ReturnsRateLimitedWithParsedRetryAfter()
    {
        // Arrange — known open issue with persistent 429s; soft retry-later state.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.TooManyRequests,
            ClaudeTestData.ReadFixture("error.429.json"),
            new Dictionary<string, string> { ["Retry-After"] = "120" });
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.RateLimited, snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(120), snapshot.RetryAfter);
        Assert.Single(handler.Requests); // 429 is not retried within the same poll
    }

    [Fact]
    public async Task GetUsageAsync_RateLimitedWithoutRetryAfterHeader_FallsBackToMinPollInterval()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.TooManyRequests, ClaudeTestData.ReadFixture("error.429.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.RateLimited, snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(180), snapshot.RetryAfter);
    }

    [Fact]
    public async Task GetUsageAsync_MalformedResponseBody_ReturnsUnavailableInsteadOfThrowing()
    {
        // Arrange — 200 with truncated JSON: untrusted input must not crash the widget.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.malformed.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Unavailable, snapshot.State);
    }

    [Fact]
    public async Task GetUsageAsync_TransientServerError_RetriesWithBackoffAndSucceeds()
    {
        // Arrange — 500 then 200; MaxRetries default 2, RetryBaseDelay zero in tests.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Ok, snapshot.State);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetUsageAsync_PersistentNetworkFailure_ReturnsUnavailableAfterExhaustingRetries()
    {
        // Arrange — 1 initial attempt + MaxRetries (2) = 3 failed sends, then Unavailable.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("connection reset (test)"));
        handler.EnqueueException(new HttpRequestException("connection reset (test)"));
        handler.EnqueueException(new HttpRequestException("connection reset (test)"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Unavailable, snapshot.State);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task GetUsageAsync_FailureSnapshot_NeverContainsTokenText()
    {
        // Arrange — Security Standards: redaction in all diagnostic output.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, ClaudeTestData.ReadFixture("error.401.json"));
        handler.Enqueue(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        handler.Enqueue(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);
        var text = snapshot.ToString();

        // Assert
        Assert.DoesNotContain(ClaudeTestData.FakeAccessToken, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ClaudeTestData.FakeRefreshToken, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ClaudeTestData.FakeRefreshedAccessToken, text, StringComparison.OrdinalIgnoreCase);
    }
}
