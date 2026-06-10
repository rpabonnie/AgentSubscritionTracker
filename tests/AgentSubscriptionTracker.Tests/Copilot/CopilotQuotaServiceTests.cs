// SPEC-0002 §4–§6 — CopilotQuotaService tests: verified request shape, response
// mapping, provider states (NotSignedIn/Forbidden/RateLimited/Unavailable),
// caching + debounce (>= 30 s), Retry-After, last-good fallback, and redaction.
// All HTTP is stubbed (no live network); tokens are fake by policy.

using System.Net;
using AgentSubscriptionTracker.App.Models;
using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.Tests.Copilot;

public sealed class CopilotQuotaServiceTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 6, 10, 22, 0, 0, TimeSpan.Zero);
    private const string FakeToken = "fake-token-for-tests";

    private static CopilotToken DefaultToken => new(FakeToken, CopilotTokenSource.CredentialManager);

    private static (CopilotQuotaService Service, StubHttpMessageHandler Handler, FakeTimeProvider Clock) CreateService(
        CopilotToken? token,
        CopilotQuotaServiceOptions? options = null)
    {
        var handler = new StubHttpMessageHandler();
        var clock = new FakeTimeProvider(StartTime);
        var service = new CopilotQuotaService(new FakeCopilotTokenProvider(token), handler, clock, options);
        return (service, handler, clock);
    }

    // Failure-path tests disable retries so the stub queue stays deterministic
    // (retry backoff delays are TimeProvider-driven per SPEC-0002 §5).
    private static CopilotQuotaServiceOptions NoRetryOptions => new() { MaxRetries = 0 };

    [Fact]
    public async Task SendsVerifiedRequestShapeAndHeaders()
    {
        // Arrange
        var (service, handler, _) = CreateService(DefaultToken);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_ok.json"));

            // Act
            _ = await service.GetSnapshotAsync();

            // Assert — SPEC-0002 §4 / research-verified contract.
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(new Uri("https://api.github.com/copilot_internal/user"), request.RequestUri);
            Assert.Equal("token", request.Headers.Authorization?.Scheme);   // 'token', NOT 'Bearer'
            Assert.Equal(FakeToken, request.Headers.Authorization?.Parameter);
            Assert.Equal("vscode/1.104.1", Assert.Single(request.Headers.GetValues("Editor-Version")));
            Assert.Equal("copilot-chat/0.26.7", Assert.Single(request.Headers.GetValues("Editor-Plugin-Version")));
            Assert.Equal("vscode-chat", Assert.Single(request.Headers.GetValues("Copilot-Integration-Id")));
            Assert.Contains("GitHubCopilotChat/0.26.7", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
            Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/json");
            Assert.False(request.Headers.Contains("X-GitHub-Api-Version")); // unverified for this endpoint — must not be sent
        }
    }

    [Fact]
    public async Task MapsGoldenFixtureToSnapshot()
    {
        // Arrange
        var (service, handler, _) = CreateService(DefaultToken);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_ok.json"));

            // Act
            var snapshot = await service.GetSnapshotAsync();

            // Assert
            Assert.Equal(CopilotProviderState.Ok, snapshot.State);
            Assert.Null(snapshot.StatusMessage);
            Assert.False(snapshot.IsFromCache);
            Assert.Equal(StartTime, snapshot.RetrievedAt);
            Assert.Equal("individual_pro", snapshot.Plan);
            Assert.Equal(new DateOnly(2026, 7, 1), snapshot.QuotaResetDate);

            Assert.NotNull(snapshot.PremiumInteractions);
            Assert.Equal("premium_interactions", snapshot.PremiumInteractions.Key);
            Assert.Equal("Premium requests", snapshot.PremiumInteractions.DisplayName);
            Assert.Equal(1500, snapshot.PremiumInteractions.Entitlement);
            Assert.Equal(1187, snapshot.PremiumInteractions.Remaining);
            Assert.Equal(79.13, snapshot.PremiumInteractions.PercentRemaining);
            Assert.False(snapshot.PremiumInteractions.Unlimited);
            Assert.Equal(0, snapshot.PremiumInteractions.OverageCount);
            Assert.True(snapshot.PremiumInteractions.OveragePermitted);
            Assert.Equal(313, snapshot.PremiumInteractions.Used); // 1500 - 1187

            Assert.NotNull(snapshot.Chat);
            Assert.Equal("Chat", snapshot.Chat.DisplayName);
            Assert.NotNull(snapshot.Completions);
            Assert.Equal("Code completions", snapshot.Completions.DisplayName);
        }
    }

    [Fact]
    public async Task UnlimitedBucketIgnoresNumericFields()
    {
        // Arrange — chat/completions are unlimited in the golden fixture.
        var (service, handler, _) = CreateService(DefaultToken);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_ok.json"));

            // Act
            var snapshot = await service.GetSnapshotAsync();

            // Assert — UI must render "Unlimited"; Used must not be computed.
            Assert.NotNull(snapshot.Chat);
            Assert.True(snapshot.Chat.Unlimited);
            Assert.Null(snapshot.Chat.Used);
            Assert.NotNull(snapshot.Completions);
            Assert.True(snapshot.Completions.Unlimited);
            Assert.Null(snapshot.Completions.Used);
        }
    }

    [Fact]
    public async Task MissingBucketYieldsNullBucketAndStateOk()
    {
        // Arrange — schema is unversioned; absent buckets must never throw.
        var (service, handler, _) = CreateService(DefaultToken);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_missing_premium.json"));

            // Act
            var snapshot = await service.GetSnapshotAsync();

            // Assert
            Assert.Equal(CopilotProviderState.Ok, snapshot.State);
            Assert.Null(snapshot.PremiumInteractions);
            Assert.NotNull(snapshot.Chat);
            Assert.Equal(21, snapshot.Chat.Remaining);
            Assert.NotNull(snapshot.Completions);
            Assert.Equal(1800, snapshot.Completions.Remaining);
        }
    }

    [Fact]
    public async Task NullAndMissingFieldsAreToleratedWithoutThrowing()
    {
        // Arrange
        var (service, handler, _) = CreateService(DefaultToken);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_null_fields.json"));

            // Act
            var snapshot = await service.GetSnapshotAsync();

            // Assert — nulls map to null model properties, state remains Ok.
            Assert.Equal(CopilotProviderState.Ok, snapshot.State);
            Assert.Null(snapshot.Plan);
            Assert.Null(snapshot.QuotaResetDate);
            Assert.NotNull(snapshot.PremiumInteractions);
            Assert.Null(snapshot.PremiumInteractions.Entitlement);
            Assert.Null(snapshot.PremiumInteractions.Remaining);
            Assert.Null(snapshot.PremiumInteractions.PercentRemaining);
            Assert.Null(snapshot.PremiumInteractions.Used);
        }
    }

    [Fact]
    public async Task MalformedJsonYieldsUnavailable()
    {
        // Arrange
        var (service, handler, _) = CreateService(DefaultToken, NoRetryOptions);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_malformed.json"));

            // Act
            var snapshot = await service.GetSnapshotAsync();

            // Assert
            Assert.Equal(CopilotProviderState.Unavailable, snapshot.State);
            Assert.NotNull(snapshot.StatusMessage);
        }
    }

    [Fact]
    public async Task MissingTokenYieldsNotSignedInWithoutAnyHttpCall()
    {
        // Arrange — empty discovery chain (the real state of this machine per research).
        var (service, handler, _) = CreateService(token: null);
        using (service)
        {
            // Act
            var snapshot = await service.GetSnapshotAsync();

            // Assert — must short-circuit before HTTP and guide the user to sign in.
            Assert.Equal(CopilotProviderState.NotSignedIn, snapshot.State);
            Assert.Equal(0, handler.CallCount);
            Assert.NotNull(snapshot.StatusMessage);
            Assert.Contains("Copilot CLI", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Unauthorized401YieldsNotSignedInAndNeverLeaksToken()
    {
        // Arrange — expired/revoked token.
        var (service, handler, _) = CreateService(DefaultToken, NoRetryOptions);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.Unauthorized, CopilotFixtures.Read("error_401.json"));

            // Act
            var snapshot = await service.GetSnapshotAsync();

            // Assert — redaction: the token value must never appear in user-facing text.
            Assert.Equal(CopilotProviderState.NotSignedIn, snapshot.State);
            Assert.NotNull(snapshot.StatusMessage);
            Assert.DoesNotContain(FakeToken, snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Copilot CLI", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Forbidden403YieldsForbiddenState()
    {
        // Arrange — known behavior for stale Neovim hosts.json tokens (research).
        var (service, handler, _) = CreateService(DefaultToken, NoRetryOptions);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.Forbidden, """{ "message": "forbidden" }""");

            // Act
            var snapshot = await service.GetSnapshotAsync();

            // Assert
            Assert.Equal(CopilotProviderState.Forbidden, snapshot.State);
            Assert.NotNull(snapshot.StatusMessage);
            Assert.DoesNotContain(FakeToken, snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RateLimited429HonorsRetryAfterBeyondDebounceWindow()
    {
        // Arrange — Retry-After: 120 s, longer than the 30 s debounce window.
        var (service, handler, clock) = CreateService(DefaultToken, NoRetryOptions);
        using (service)
        {
            handler.EnqueueJson(
                HttpStatusCode.TooManyRequests,
                """{ "message": "API rate limit exceeded" }""",
                response => response.Headers.Add("Retry-After", "120"));

            // Act 1 — hits the network, gets 429.
            var first = await service.GetSnapshotAsync();

            // Act 2 — 60 s later: past debounce but inside Retry-After → no request.
            clock.Advance(TimeSpan.FromSeconds(60));
            var second = await service.GetSnapshotAsync();
            var callsWhileRateLimited = handler.CallCount;

            // Act 3 — 150 s total: Retry-After elapsed → request goes out again.
            clock.Advance(TimeSpan.FromSeconds(90));
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_ok.json"));
            var third = await service.GetSnapshotAsync();

            // Assert
            Assert.Equal(CopilotProviderState.RateLimited, first.State);
            Assert.Equal(CopilotProviderState.RateLimited, second.State);
            Assert.True(second.IsFromCache);
            Assert.Equal(1, callsWhileRateLimited); // no new request while Retry-After is pending
            Assert.Equal(CopilotProviderState.Ok, third.State);
            Assert.Equal(2, handler.CallCount);
        }
    }

    [Fact]
    public async Task RepeatedCallsWithinDebounceWindowReturnCachedSnapshot()
    {
        // Arrange
        var (service, handler, clock) = CreateService(DefaultToken);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_ok.json"));

            // Act — hover-spam: second call 10 s later must not hit the network.
            var first = await service.GetSnapshotAsync();
            clock.Advance(TimeSpan.FromSeconds(10));
            var second = await service.GetSnapshotAsync();

            // Assert
            Assert.Equal(1, handler.CallCount);
            Assert.False(first.IsFromCache);
            Assert.True(second.IsFromCache);
            Assert.Equal(StartTime, second.RetrievedAt); // data age preserved for the UI
            Assert.Equal(first.PremiumInteractions?.Remaining, second.PremiumInteractions?.Remaining);
        }
    }

    [Fact]
    public async Task CallAfterDebounceWindowRefreshesFromNetwork()
    {
        // Arrange
        var (service, handler, clock) = CreateService(DefaultToken);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_ok.json"));
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_ok.json"));

            // Act — 31 s later the 30 s debounce window has elapsed.
            _ = await service.GetSnapshotAsync();
            clock.Advance(TimeSpan.FromSeconds(31));
            var second = await service.GetSnapshotAsync();

            // Assert
            Assert.Equal(2, handler.CallCount);
            Assert.False(second.IsFromCache);
            Assert.Equal(StartTime + TimeSpan.FromSeconds(31), second.RetrievedAt);
        }
    }

    [Fact]
    public async Task FailedRefreshAfterSuccessKeepsLastGoodDataWithErrorState()
    {
        // Arrange
        var (service, handler, clock) = CreateService(DefaultToken, NoRetryOptions);
        using (service)
        {
            handler.EnqueueJson(HttpStatusCode.OK, CopilotFixtures.Read("user_ok.json"));
            handler.EnqueueJson(HttpStatusCode.InternalServerError, """{ "message": "boom" }""");

            // Act
            var first = await service.GetSnapshotAsync();
            clock.Advance(TimeSpan.FromSeconds(31));
            var second = await service.GetSnapshotAsync();

            // Assert — stale-but-useful: error state, last good numbers preserved.
            Assert.Equal(CopilotProviderState.Ok, first.State);
            Assert.Equal(CopilotProviderState.Unavailable, second.State);
            Assert.True(second.IsFromCache);
            Assert.NotNull(second.PremiumInteractions);
            Assert.Equal(313, second.PremiumInteractions.Used);
            Assert.Equal("individual_pro", second.Plan);
        }
    }

    [Fact]
    public void ConstructorRejectsNonAllowlistedBaseAddress()
    {
        // Arrange — CLAUDE.md allowlist: api.github.com only for this service.
        using var handler = new StubHttpMessageHandler();
        var options = new CopilotQuotaServiceOptions { BaseAddress = new Uri("https://evil.example.com/") };

        // Act + Assert
        Assert.Throws<ArgumentException>(() =>
            new CopilotQuotaService(new FakeCopilotTokenProvider(DefaultToken), handler, new FakeTimeProvider(StartTime), options));
    }
}
