// SPEC-0003 §4.1–§4.3 — CopilotQuotaSnapshot → ProviderViewModel mapping.
// Spec-phase stubs: they reference view-model types that do not exist yet, so the
// test project intentionally does not compile until TASK-008 implements SPEC-0003.

using System.Text.Json;
using AgentSubscriptionTracker.App.Models;
using AgentSubscriptionTracker.App.ViewModels;

namespace AgentSubscriptionTracker.Tests.Tray;

public sealed class ProviderViewModelCopilotTests
{
    private static readonly DateTimeOffset T0 = TrayTestData.T0;

    private static ProviderViewModel Map(string fixtureName) =>
        ProviderViewModel.ForCopilot(TrayFixtures.LoadCopilotSnapshot(fixtureName), T0);

    private static CopilotQuotaSnapshot OkSnapshotWithPremium(CopilotQuotaBucket premium) => new()
    {
        State = CopilotProviderState.Ok,
        RetrievedAt = T0,
        PremiumInteractions = premium,
    };

    [Fact]
    public void ForCopilotMapsBucketsInOrderUsingDisplayNames()
    {
        var vm = Map("copilot_snapshot_ok.json");

        Assert.Equal("GitHub Copilot", vm.ProviderName);
        Assert.Equal(ProviderDisplayState.Ok, vm.DisplayState);
        Assert.Null(vm.StateMessage);
        Assert.Collection(
            vm.Bars,
            bar => Assert.Equal("Premium requests", bar.Label),
            bar => Assert.Equal("Chat", bar.Label),
            bar => Assert.Equal("Code completions", bar.Label));
    }

    [Fact]
    public void ForCopilotRendersUnlimitedBuckets()
    {
        var vm = Map("copilot_snapshot_ok.json");

        var chat = vm.Bars[1];
        Assert.True(chat.IsUnlimited);
        Assert.Equal("Unlimited", chat.ValueText);
        Assert.Equal(0.0, chat.PercentUsed, precision: 5);
        Assert.Equal(UsageSeverity.Normal, chat.Severity);
        Assert.Null(chat.ResetText);
    }

    [Fact]
    public void ForCopilotComputesPercentUsedFromPercentRemaining()
    {
        var vm = Map("copilot_snapshot_ok.json");

        var premium = vm.Bars[0];
        Assert.Equal(37.0, premium.PercentUsed, precision: 5);
        Assert.Equal(UsageSeverity.Normal, premium.Severity);
    }

    [Fact]
    public void ForCopilotBuildsCountsValueText()
    {
        var vm = Map("copilot_snapshot_ok.json");

        Assert.Equal("945 of 1500 left", vm.Bars[0].ValueText);
    }

    [Fact]
    public void ForCopilotFallsBackToPercentValueTextWhenCountsMissing()
    {
        var vm = Map("copilot_snapshot_missing_fields.json");

        var premium = Assert.Single(vm.Bars);
        Assert.Equal("35% left", premium.ValueText);
        Assert.Equal(65.0, premium.PercentUsed, precision: 5);
        Assert.Null(vm.PlanText);
        Assert.Null(vm.FooterText);
    }

    [Fact]
    public void ForCopilotDerivesPercentFromCountsWhenPercentRemainingMissing()
    {
        var snapshot = OkSnapshotWithPremium(new CopilotQuotaBucket
        {
            Key = "premium_interactions",
            DisplayName = "Premium requests",
            Entitlement = 200,
            Remaining = 50,
        });

        var vm = ProviderViewModel.ForCopilot(snapshot, T0);

        var premium = Assert.Single(vm.Bars);
        Assert.Equal(75.0, premium.PercentUsed, precision: 5);
        Assert.Equal("50 of 200 left", premium.ValueText);
        Assert.Equal(UsageSeverity.Warning, premium.Severity);
    }

    [Fact]
    public void ForCopilotOmitsBucketWithNothingDisplayable()
    {
        var snapshot = OkSnapshotWithPremium(new CopilotQuotaBucket
        {
            Key = "premium_interactions",
            DisplayName = "Premium requests",
        });

        var vm = ProviderViewModel.ForCopilot(snapshot, T0);

        Assert.Empty(vm.Bars);
        Assert.False(vm.HasData);
    }

    [Fact]
    public void ForCopilotBuildsMonthlyResetFooter()
    {
        var vm = Map("copilot_snapshot_ok.json");

        Assert.Equal("Resets Jul 1", vm.FooterText);
    }

    [Fact]
    public void ForCopilotAppendsOverageToFooter()
    {
        var vm = Map("copilot_snapshot_overage.json");

        Assert.Equal("Resets Jul 1 · 12 overage used", vm.FooterText);
    }

    [Theory]
    [InlineData("individual", "Individual")]
    [InlineData("individual_pro", "Pro")]
    [InlineData("business", "Business")]
    [InlineData("some_future_plan", "some_future_plan")]
    [InlineData(null, null)]
    public void ForCopilotMapsPlanText(string? plan, string? expected)
    {
        var snapshot = new CopilotQuotaSnapshot
        {
            State = CopilotProviderState.Ok,
            RetrievedAt = T0,
            Plan = plan,
        };

        var vm = ProviderViewModel.ForCopilot(snapshot, T0);

        Assert.Equal(expected, vm.PlanText);
    }

    [Fact]
    public void ForCopilotCriticalWhenAlmostExhausted()
    {
        var vm = Map("copilot_snapshot_low_premium.json");

        var premium = vm.Bars[0];
        Assert.Equal(92.0, premium.PercentUsed, precision: 5);
        Assert.Equal("120 of 1500 left", premium.ValueText);
        Assert.Equal(UsageSeverity.Critical, premium.Severity);
        Assert.Equal("Pro", vm.PlanText);
    }

    [Fact]
    public void ForCopilotNotSignedInPrefersServiceStatusMessage()
    {
        var vm = Map("copilot_snapshot_not_signed_in.json");

        Assert.Equal(ProviderDisplayState.SignedOut, vm.DisplayState);
        Assert.Equal("Sign in via Copilot CLI (or JetBrains/Neovim plugin)", vm.StateMessage);
        Assert.False(vm.HasData);
    }

    [Fact]
    public void ForCopilotNotSignedInFallsBackWhenServiceMessageMissing()
    {
        var snapshot = new CopilotQuotaSnapshot
        {
            State = CopilotProviderState.NotSignedIn,
            RetrievedAt = T0,
        };

        var vm = ProviderViewModel.ForCopilot(snapshot, T0);

        Assert.Equal("Sign in via GitHub Copilot CLI", vm.StateMessage);
    }

    [Fact]
    public void ForCopilotForbiddenKeepsCachedBarsAndShowsMessage()
    {
        var vm = Map("copilot_snapshot_forbidden_cached.json");

        Assert.Equal(ProviderDisplayState.AttentionRequired, vm.DisplayState);
        Assert.Equal("Copilot token rejected — sign in again via Copilot CLI", vm.StateMessage);
        Assert.True(vm.HasData);
        Assert.True(vm.IsStale);
        Assert.Single(vm.Bars);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 11, 58, 30, TimeSpan.Zero), vm.RetrievedAtUtc);
    }

    [Fact]
    public void ForCopilotUnavailableFallbackMessage()
    {
        var snapshot = new CopilotQuotaSnapshot
        {
            State = CopilotProviderState.Unavailable,
            RetrievedAt = T0,
        };

        var vm = ProviderViewModel.ForCopilot(snapshot, T0);

        Assert.Equal(ProviderDisplayState.Unavailable, vm.DisplayState);
        Assert.Equal("Copilot quota temporarily unavailable", vm.StateMessage);
    }

    [Fact]
    public void MalformedCopilotFixtureFailsStrictDeserialization()
    {
        // Documents the layering: malformed wire data must be handled by the service
        // (SPEC-0002 → Unavailable); snapshot deserialization itself is strict.
        Assert.ThrowsAny<JsonException>(() => TrayFixtures.LoadCopilotSnapshot("copilot_snapshot_malformed.json"));
    }
}
