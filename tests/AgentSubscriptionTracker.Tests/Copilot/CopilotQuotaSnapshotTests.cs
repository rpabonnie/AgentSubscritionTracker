// SPEC-0002 §2 — model-level tests: CopilotQuotaBucket.Used math, unlimited
// semantics, and CopilotQuotaSnapshot.GetTimeUntilReset with injected TimeProvider.

using AgentSubscriptionTracker.App.Models;

namespace AgentSubscriptionTracker.Tests.Copilot;

public sealed class CopilotQuotaSnapshotTests
{
    private static CopilotQuotaBucket PremiumBucket(int? entitlement, int? remaining, bool unlimited = false) => new()
    {
        Key = "premium_interactions",
        DisplayName = "Premium requests",
        Entitlement = entitlement,
        Remaining = remaining,
        Unlimited = unlimited,
    };

    private static CopilotQuotaSnapshot SnapshotWithResetDate(DateOnly? resetDate) => new()
    {
        State = CopilotProviderState.Ok,
        QuotaResetDate = resetDate,
    };

    [Fact]
    public void UsedIsEntitlementMinusRemaining()
    {
        // Arrange
        var bucket = PremiumBucket(entitlement: 1500, remaining: 1187);

        // Act
        var used = bucket.Used;

        // Assert
        Assert.Equal(313, used);
    }

    [Fact]
    public void UsedIsNullWhenUnlimited()
    {
        // Arrange — unlimited buckets must ignore numeric fields (display "Unlimited").
        var bucket = PremiumBucket(entitlement: 0, remaining: 0, unlimited: true);

        // Act + Assert
        Assert.Null(bucket.Used);
    }

    [Fact]
    public void UsedIsNullWhenEntitlementOrRemainingMissing()
    {
        // Arrange + Act + Assert
        Assert.Null(PremiumBucket(entitlement: null, remaining: 1187).Used);
        Assert.Null(PremiumBucket(entitlement: 1500, remaining: null).Used);
    }

    [Fact]
    public void GetTimeUntilResetComputesUtcDeltaFromInjectedClock()
    {
        // Arrange — reset moment is quota_reset_date at 00:00:00 UTC.
        var snapshot = SnapshotWithResetDate(new DateOnly(2026, 7, 1));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 10, 22, 0, 0, TimeSpan.Zero));

        // Act
        var untilReset = snapshot.GetTimeUntilReset(clock);

        // Assert — 2026-07-01T00:00Z - 2026-06-10T22:00Z = 20 days 2 hours.
        Assert.Equal(TimeSpan.FromDays(20) + TimeSpan.FromHours(2), untilReset);
    }

    [Fact]
    public void GetTimeUntilResetClampsToZeroWhenDateIsInThePast()
    {
        // Arrange — stale cached snapshot after the reset moment.
        var snapshot = SnapshotWithResetDate(new DateOnly(2026, 7, 1));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 2, 8, 30, 0, TimeSpan.Zero));

        // Act
        var untilReset = snapshot.GetTimeUntilReset(clock);

        // Assert
        Assert.Equal(TimeSpan.Zero, untilReset);
    }

    [Fact]
    public void GetTimeUntilResetReturnsNullWithoutResetDate()
    {
        // Arrange
        var snapshot = SnapshotWithResetDate(null);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 10, 22, 0, 0, TimeSpan.Zero));

        // Act + Assert
        Assert.Null(snapshot.GetTimeUntilReset(clock));
    }
}
