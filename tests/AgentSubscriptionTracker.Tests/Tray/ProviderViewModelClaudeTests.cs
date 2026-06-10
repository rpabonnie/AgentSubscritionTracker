// SPEC-0003 §4.1–§4.3 — ClaudeUsageSnapshot → ProviderViewModel mapping.
// Spec-phase stubs: they reference view-model types that do not exist yet, so the
// test project intentionally does not compile until TASK-008 implements SPEC-0003.

using System.Text.Json;
using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.App.ViewModels;

namespace AgentSubscriptionTracker.Tests.Tray;

public sealed class ProviderViewModelClaudeTests
{
    private static readonly DateTimeOffset T0 = TrayTestData.T0;

    private static ProviderViewModel Map(string fixtureName) =>
        ProviderViewModel.ForClaude(TrayFixtures.LoadClaudeSnapshot(fixtureName), T0);

    private static ClaudeUsageSnapshot OkSnapshotWithFiveHour(double percentUsed) => new()
    {
        State = ClaudeProviderState.Ok,
        RetrievedAt = T0,
        FiveHour = new ClaudeUsageBucket { PercentUsed = percentUsed },
    };

    [Fact]
    public void ForClaudeMapsAllBucketsToBarsInOrder()
    {
        var vm = Map("claude_snapshot_ok.json");

        Assert.Equal("Claude", vm.ProviderName);
        Assert.Equal(ProviderDisplayState.Ok, vm.DisplayState);
        Assert.True(vm.HasData);
        Assert.Null(vm.StateMessage);
        Assert.Collection(
            vm.Bars,
            bar => Assert.Equal("5-hour session", bar.Label),
            bar => Assert.Equal("Weekly (all models)", bar.Label),
            bar => Assert.Equal("Weekly (Opus)", bar.Label),
            bar => Assert.Equal("Weekly (Sonnet)", bar.Label));
    }

    [Fact]
    public void ForClaudeFormatsPercentUsedAndResetCountdown()
    {
        var vm = Map("claude_snapshot_ok.json");

        var fiveHour = vm.Bars[0];
        Assert.Equal(63.0, fiveHour.PercentUsed, precision: 5);
        Assert.Equal("63% used", fiveHour.ValueText);
        Assert.Equal("Resets in 2 h 30 m", fiveHour.ResetText);
        Assert.False(fiveHour.IsUnlimited);

        var sevenDay = vm.Bars[1];
        Assert.Equal("42% used", sevenDay.ValueText); // 41.5 rounds away from zero
        Assert.Equal("Resets in 3 d 19 h", sevenDay.ResetText);
    }

    [Fact]
    public void ForClaudeOmitsResetTextWhenResetsAtMissing()
    {
        var vm = Map("claude_snapshot_partial.json");

        Assert.Equal(2, vm.Bars.Count);
        Assert.NotNull(vm.Bars[0].ResetText);  // fiveHour has resetsAt
        Assert.Null(vm.Bars[1].ResetText);     // sevenDay has none
    }

    [Theory]
    [InlineData(0.0, UsageSeverity.Normal)]
    [InlineData(74.9, UsageSeverity.Normal)]
    [InlineData(75.0, UsageSeverity.Warning)]
    [InlineData(89.9, UsageSeverity.Warning)]
    [InlineData(90.0, UsageSeverity.Critical)]
    [InlineData(100.0, UsageSeverity.Critical)]
    public void ForClaudeAssignsSeverityFromUnroundedPercent(double percentUsed, UsageSeverity expected)
    {
        var vm = ProviderViewModel.ForClaude(OkSnapshotWithFiveHour(percentUsed), T0);

        var bar = Assert.Single(vm.Bars);
        Assert.Equal(expected, bar.Severity);
    }

    [Fact]
    public void ForClaudeClampsOutOfRangePercentUsed()
    {
        var vm = ProviderViewModel.ForClaude(OkSnapshotWithFiveHour(140.0), T0);

        var bar = Assert.Single(vm.Bars);
        Assert.Equal(100.0, bar.PercentUsed, precision: 5);
        Assert.Equal("100% used", bar.ValueText);
        Assert.Equal(UsageSeverity.Critical, bar.Severity);
    }

    [Fact]
    public void ForClaudeOmitsNullBuckets()
    {
        var vm = Map("claude_snapshot_partial.json");

        Assert.Collection(
            vm.Bars,
            bar => Assert.Equal("5-hour session", bar.Label),
            bar => Assert.Equal("Weekly (all models)", bar.Label));
    }

    [Fact]
    public void ForClaudeBuildsExtraUsageFooter()
    {
        var vm = Map("claude_snapshot_ok.json");

        Assert.Equal("Extra usage: $4.12 of $25.00", vm.FooterText);
    }

    [Fact]
    public void ForClaudeOmitsFooterWhenExtraUsageMissing()
    {
        var vm = Map("claude_snapshot_partial.json");

        Assert.Null(vm.FooterText);
    }

    [Fact]
    public void ForClaudeOmitsFooterWhenExtraUsageDisabled()
    {
        var snapshot = OkSnapshotWithFiveHour(10.0) with
        {
            ExtraUsage = new ClaudeExtraUsage { IsEnabled = false, UsedCreditsCents = 999m },
        };

        var vm = ProviderViewModel.ForClaude(snapshot, T0);

        Assert.Null(vm.FooterText);
    }

    [Fact]
    public void ForClaudeOmitsMonthlyLimitPartWhenAbsent()
    {
        var snapshot = OkSnapshotWithFiveHour(10.0) with
        {
            ExtraUsage = new ClaudeExtraUsage { IsEnabled = true, UsedCreditsCents = 412m, Currency = "USD" },
        };

        var vm = ProviderViewModel.ForClaude(snapshot, T0);

        Assert.Equal("Extra usage: $4.12", vm.FooterText);
    }

    [Theory]
    [InlineData("max", "Max")]
    [InlineData("pro", "Pro")]
    [InlineData(null, null)]
    public void ForClaudeMapsSubscriptionTypeToPlanText(string? subscriptionType, string? expected)
    {
        var snapshot = OkSnapshotWithFiveHour(10.0) with { SubscriptionType = subscriptionType };

        var vm = ProviderViewModel.ForClaude(snapshot, T0);

        Assert.Equal(expected, vm.PlanText);
    }

    [Fact]
    public void ForClaudeNotSignedInShowsSignInMessageAndNoBars()
    {
        var vm = Map("claude_snapshot_not_signed_in.json");

        Assert.Equal(ProviderDisplayState.SignedOut, vm.DisplayState);
        Assert.Equal("Sign in via Claude Code CLI", vm.StateMessage);
        Assert.False(vm.HasData);
        Assert.Empty(vm.Bars);
    }

    [Fact]
    public void ForClaudeTokenExpiredKeepsCachedBarsAndShowsMessage()
    {
        var vm = Map("claude_snapshot_token_expired_cached.json");

        Assert.Equal(ProviderDisplayState.AttentionRequired, vm.DisplayState);
        Assert.Equal("Claude session expired — run /login in Claude Code", vm.StateMessage);
        Assert.True(vm.HasData);
        Assert.Equal(2, vm.Bars.Count);
        Assert.True(vm.IsStale);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 11, 57, 0, TimeSpan.Zero), vm.RetrievedAtUtc);
    }

    [Fact]
    public void ForClaudeRateLimitedShowsRetryLaterMessage()
    {
        var snapshot = new ClaudeUsageSnapshot
        {
            State = ClaudeProviderState.RateLimited,
            RetrievedAt = T0,
        };

        var vm = ProviderViewModel.ForClaude(snapshot, T0);

        Assert.Equal(ProviderDisplayState.RateLimited, vm.DisplayState);
        Assert.Equal("Rate limited — will retry later", vm.StateMessage);
    }

    [Fact]
    public void ForClaudeUnavailableShowsUnavailableMessage()
    {
        var snapshot = new ClaudeUsageSnapshot
        {
            State = ClaudeProviderState.Unavailable,
            RetrievedAt = T0,
        };

        var vm = ProviderViewModel.ForClaude(snapshot, T0);

        Assert.Equal(ProviderDisplayState.Unavailable, vm.DisplayState);
        Assert.Equal("Claude usage temporarily unavailable", vm.StateMessage);
    }

    [Fact]
    public void CreateEmptyIsLoadingPlaceholder()
    {
        var vm = ProviderViewModel.CreateEmpty("Claude");

        Assert.Equal("Claude", vm.ProviderName);
        Assert.Equal(ProviderDisplayState.Loading, vm.DisplayState);
        Assert.Equal("Loading…", vm.StateMessage);
        Assert.False(vm.HasData);
        Assert.Empty(vm.Bars);
        Assert.Null(vm.RetrievedAtUtc);
    }

    [Fact]
    public void MalformedClaudeFixtureFailsStrictDeserialization()
    {
        // Documents the layering: malformed wire data must be handled by the service
        // (SPEC-0001 → Unavailable); snapshot deserialization itself is strict.
        Assert.ThrowsAny<JsonException>(() => TrayFixtures.LoadClaudeSnapshot("claude_snapshot_malformed.json"));
    }
}
