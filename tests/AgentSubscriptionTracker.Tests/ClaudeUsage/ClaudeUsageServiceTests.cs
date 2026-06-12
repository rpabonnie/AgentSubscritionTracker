using System.Net;
using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.Tests.TestSupport;

namespace AgentSubscriptionTracker.Tests.ClaudeUsage;

/// <summary>
/// SPEC-0001 §4.1 step 4 / §4.3 — happy-path request shape and response mapping for
/// GET api.anthropic.com/api/oauth/usage. No live network: scripted FakeHttpMessageHandler
/// + golden-file fixtures.
/// </summary>
public sealed class ClaudeUsageServiceTests
{
    [Fact]
    public async Task GetUsageAsync_FullResponse_ReturnsOkAndMapsAllBuckets()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Ok, snapshot.State);
        Assert.False(snapshot.IsFromCache);
        Assert.Equal(ClaudeTestData.TestNow, snapshot.RetrievedAt);
        Assert.Equal("max", snapshot.SubscriptionType);

        Assert.NotNull(snapshot.FiveHour);
        Assert.Equal(42.5, snapshot.FiveHour.PercentUsed);
        Assert.Equal(57.5, snapshot.FiveHour.PercentRemaining);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 15, 0, 0, TimeSpan.Zero), snapshot.FiveHour.ResetsAt);

        Assert.NotNull(snapshot.SevenDay);
        Assert.Equal(80, snapshot.SevenDay.PercentUsed);
        Assert.Equal(20, snapshot.SevenDay.PercentRemaining);
        Assert.Equal(new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero), snapshot.SevenDay.ResetsAt);

        Assert.NotNull(snapshot.SevenDayOpus);
        Assert.Equal(12.25, snapshot.SevenDayOpus.PercentUsed);

        Assert.NotNull(snapshot.SevenDaySonnet);
        Assert.Equal(55, snapshot.SevenDaySonnet.PercentUsed);
    }

    [Fact]
    public async Task GetUsageAsync_FullResponse_MapsExtraUsageSubfields()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert — credits are reported in cents (research doc).
        Assert.NotNull(snapshot.ExtraUsage);
        Assert.True(snapshot.ExtraUsage.IsEnabled);
        Assert.Equal(1250m, snapshot.ExtraUsage.UsedCreditsCents);
        Assert.Equal(5000m, snapshot.ExtraUsage.MonthlyLimitCents);
        Assert.Equal("USD", snapshot.ExtraUsage.Currency);
    }

    [Fact]
    public async Task GetUsageAsync_SendsRequiredHeadersToUsageEndpoint()
    {
        // Arrange — research doc: Authorization Bearer, anthropic-beta oauth-2025-04-20,
        // and a claude-code/<version> User-Agent (without it: persistent 429s).
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        _ = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://api.anthropic.com/api/oauth/usage"), request.Uri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(ClaudeTestData.FakeAccessToken, request.AuthorizationParameter);
        Assert.Equal("oauth-2025-04-20", request.AnthropicBeta);
        Assert.StartsWith("claude-code/", request.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_NullOpusAndSonnetBuckets_MapsToNullUnlimitedBuckets()
    {
        // Arrange — seven_day_opus / seven_day_sonnet are null for plans without
        // model-specific weekly limits ("unlimited bucket" edge case).
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.null-buckets.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Ok, snapshot.State);
        Assert.NotNull(snapshot.FiveHour);
        Assert.NotNull(snapshot.SevenDay);
        Assert.Null(snapshot.SevenDayOpus);
        Assert.Null(snapshot.SevenDaySonnet);
        Assert.NotNull(snapshot.ExtraUsage);
        Assert.False(snapshot.ExtraUsage.IsEnabled);
        Assert.Null(snapshot.ExtraUsage.UsedCreditsCents);
        Assert.Null(snapshot.ExtraUsage.MonthlyLimitCents);
        Assert.Null(snapshot.ExtraUsage.Currency);
    }

    [Fact]
    public async Task GetUsageAsync_MissingAndPartialFields_ParsedDefensively()
    {
        // Arrange — five_hour has no resets_at; seven_day has no utilization (treated
        // as absent); unknown top-level fields are ignored; extra_usage missing entirely.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.missing-fields.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Ok, snapshot.State);
        Assert.NotNull(snapshot.FiveHour);
        Assert.Equal(42.5, snapshot.FiveHour.PercentUsed);
        Assert.Null(snapshot.FiveHour.ResetsAt);
        Assert.Null(snapshot.SevenDay); // no utilization value => bucket absent
        Assert.Null(snapshot.ExtraUsage);
    }

    [Fact]
    public async Task GetUsageAsync_OutOfRangeUtilization_ClampedToValidPercentRange()
    {
        // Arrange — utilization 105.3 and -5 must clamp to [0, 100].
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.out-of-range.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var snapshot = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.NotNull(snapshot.FiveHour);
        Assert.Equal(100, snapshot.FiveHour.PercentUsed);
        Assert.Equal(0, snapshot.FiveHour.PercentRemaining);
        Assert.NotNull(snapshot.SevenDay);
        Assert.Equal(0, snapshot.SevenDay.PercentUsed);
        Assert.Equal(100, snapshot.SevenDay.PercentRemaining);
    }

    [Fact]
    public void TimeUntilReset_FutureReset_ReturnsPositiveCountdown()
    {
        // Arrange — reset-time math with injected clock value (SPEC-0001 §4.3).
        var bucket = new ClaudeUsageBucket
        {
            PercentUsed = 42.5,
            ResetsAt = new DateTimeOffset(2026, 6, 10, 15, 0, 0, TimeSpan.Zero),
        };

        // Act
        var remaining = bucket.TimeUntilReset(ClaudeTestData.TestNow); // 12:00Z -> 15:00Z

        // Assert
        Assert.Equal(TimeSpan.FromHours(3), remaining);
    }

    [Fact]
    public void TimeUntilReset_PastReset_ClampsToZeroNeverNegative()
    {
        // Arrange
        var bucket = new ClaudeUsageBucket
        {
            PercentUsed = 99,
            ResetsAt = ClaudeTestData.TestNow - TimeSpan.FromMinutes(5),
        };

        // Act
        var remaining = bucket.TimeUntilReset(ClaudeTestData.TestNow);

        // Assert
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void TimeUntilReset_NoResetTimestamp_ReturnsNull()
    {
        // Arrange
        var bucket = new ClaudeUsageBucket { PercentUsed = 10, ResetsAt = null };

        // Act
        var remaining = bucket.TimeUntilReset(ClaudeTestData.TestNow);

        // Assert
        Assert.Null(remaining);
    }

    [Fact]
    public async Task GetUsageAsync_ReadsCredentialsFreshOnEveryNetworkPoll()
    {
        // Arrange — SPEC-0001 §4.1 step 2: re-read the file each poll, a running
        // Claude Code may have rotated the token in the meantime.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        _ = await service.GetUsageAsync().ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(181)); // past MinPollInterval
        _ = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(2, reader.ReadCount);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Constructor_RejectsNonAllowlistedUsageEndpoint()
    {
        // Arrange — CLAUDE.md allowlist: usage calls only to api.anthropic.com.
        var handler = new FakeHttpMessageHandler();
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        var options = ClaudeTestData.FastOptions() with
        {
            UsageEndpoint = new Uri("https://evil.example.com/api/oauth/usage"),
        };

        // Act + Assert
        Assert.Throws<ArgumentException>(() =>
            new ClaudeUsageService(reader, handler, time, options));
    }

    [Fact]
    public void Constructor_RejectsHttpUsageEndpoint()
    {
        // Arrange — HTTPS only, even on the allowlisted host.
        var handler = new FakeHttpMessageHandler();
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        var options = ClaudeTestData.FastOptions() with
        {
            UsageEndpoint = new Uri("http://api.anthropic.com/api/oauth/usage"),
        };

        // Act + Assert
        Assert.Throws<ArgumentException>(() =>
            new ClaudeUsageService(reader, handler, time, options));
    }

    [Fact]
    public void Constructor_RejectsNonAllowlistedRefreshEndpoint()
    {
        // Arrange — ADR-0002: the refresh grant (carrying the refresh token) may only
        // target platform.claude.com / console.anthropic.com.
        var handler = new FakeHttpMessageHandler();
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        var options = ClaudeTestData.FastOptions() with
        {
            TokenRefreshEndpoints =
            [
                new Uri("https://platform.claude.com/v1/oauth/token"),
                new Uri("https://attacker.example.com/v1/oauth/token"),
            ],
        };

        // Act + Assert
        Assert.Throws<ArgumentException>(() =>
            new ClaudeUsageService(reader, handler, time, options));
    }

    [Fact]
    public void Constructor_AcceptsDefaultAllowlistedEndpoints()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());

        // Act + Assert — defaults construct without throwing.
        using var service = new ClaudeUsageService(reader, handler, time, new ClaudeUsageServiceOptions());
    }
}
