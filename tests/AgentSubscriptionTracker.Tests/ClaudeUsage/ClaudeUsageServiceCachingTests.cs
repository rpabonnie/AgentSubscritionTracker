using System.Net;
using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.Tests.TestSupport;

namespace AgentSubscriptionTracker.Tests.ClaudeUsage;

/// <summary>
/// SPEC-0001 §4.1 steps 1 & 6 — minimum poll interval (180 s default), cached snapshot
/// with growing data age, and stale-data carry-over when a later poll fails. All clock
/// movement goes through the injected TimeProvider.
/// </summary>
public sealed class ClaudeUsageServiceCachingTests
{
    [Fact]
    public async Task GetUsageAsync_WithinMinPollInterval_ReturnsCachedSnapshotWithoutSecondRequest()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var first = await service.GetUsageAsync().ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(60)); // < 180 s
        var second = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Single(handler.Requests);
        Assert.False(first.IsFromCache);
        Assert.True(second.IsFromCache);
        Assert.Equal(ClaudeProviderState.Ok, second.State);
        Assert.Equal(first.RetrievedAt, second.RetrievedAt); // original fetch instant preserved
        Assert.NotNull(second.FiveHour);
        Assert.Equal(42.5, second.FiveHour.PercentUsed);
    }

    [Fact]
    public async Task GetUsageAsync_AfterMinPollIntervalElapses_PollsTheNetworkAgain()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.null-buckets.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        _ = await service.GetUsageAsync().ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(181)); // > 180 s
        var second = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(2, handler.Requests.Count);
        Assert.False(second.IsFromCache);
        Assert.Equal(ClaudeTestData.TestNow + TimeSpan.FromSeconds(181), second.RetrievedAt);
        Assert.Null(second.SevenDayOpus); // proves the second body was used
    }

    [Fact]
    public async Task DataAge_GrowsWithInjectedClockWhileServingCachedSnapshot()
    {
        // Arrange — the tooltip must show data age (CLAUDE.md hover refresh budget).
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var first = await service.GetUsageAsync().ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(90));
        var cached = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(TimeSpan.Zero, first.DataAge(ClaudeTestData.TestNow));
        Assert.Equal(TimeSpan.FromSeconds(90), cached.DataAge(time.GetUtcNow()));
    }

    [Fact]
    public async Task GetUsageAsync_FailureAfterPriorSuccess_CarriesCachedBucketsWithErrorState()
    {
        // Arrange — SPEC-0001 §4.1 step 6: stale-but-shown. Success, then (past the
        // min interval) a persistent failure: 1 attempt + 2 retries = 3 scripted 500s.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var first = await service.GetUsageAsync().ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(181));
        var degraded = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert — error state, but the previously fetched buckets are still available.
        Assert.Equal(ClaudeProviderState.Unavailable, degraded.State);
        Assert.True(degraded.IsFromCache);
        Assert.Equal(first.RetrievedAt, degraded.RetrievedAt);
        Assert.NotNull(degraded.FiveHour);
        Assert.Equal(42.5, degraded.FiveHour.PercentUsed);
        Assert.Equal(TimeSpan.FromSeconds(181), degraded.DataAge(time.GetUtcNow()));
    }

    [Fact]
    public async Task GetUsageAsync_FailedPoll_AlsoStartsMinIntervalWindow()
    {
        // Arrange — error polls must not hammer the API: after a failed poll the next
        // call inside the window is served from state without new requests.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}"); // retries of poll #1
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        var reader = new FakeClaudeCredentialsReader(ClaudeTestData.ValidCredentials());
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var first = await service.GetUsageAsync().ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(30));
        var second = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.Unavailable, first.State);
        Assert.Equal(ClaudeProviderState.Unavailable, second.State);
        Assert.Equal(3, handler.Requests.Count); // no traffic for the second call
    }

    [Fact]
    public async Task GetUsageAsync_NotSignedIn_IsNotCachedByMinIntervalGate()
    {
        // Arrange — credential discovery is a cheap local file read; once the user signs
        // in, the very next poll must pick it up rather than wait out the interval.
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ClaudeTestData.ReadFixture("usage.full.json"));
        var time = new TestTimeProvider(ClaudeTestData.TestNow);
        ClaudeOAuthCredentials? current = null;
        var reader = new FakeClaudeCredentialsReader(() => current);
        using var service = ClaudeTestData.CreateService(reader, handler, time);

        // Act
        var before = await service.GetUsageAsync().ConfigureAwait(true);
        current = ClaudeTestData.ValidCredentials(); // user signed in via Claude Code
        time.Advance(TimeSpan.FromSeconds(5)); // well inside 180 s
        var after = await service.GetUsageAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(ClaudeProviderState.NotSignedIn, before.State);
        Assert.Equal(ClaudeProviderState.Ok, after.State);
        Assert.Single(handler.Requests);
    }
}
