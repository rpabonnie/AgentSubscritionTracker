// SPEC-0003 §4.4 step 5 (amended 2026-06-11) — per-provider budget semantics: a fetch that
// exceeds PerProviderTimeout is NOT cancelled and NOT presented as Unavailable; the refresh
// pass returns (keeping the current presentation) and the fetch publishes when it lands.
// Regression coverage for the TASK-011 finding: an expired Claude token needs an OAuth
// refresh round-trip that cannot finish inside the 2 s hover budget.
//
// The budget delay runs on real timers (TestTimeProvider does not override CreateTimer),
// so these tests use a small real PerProviderTimeout instead of advancing fake time.
// awaits use ConfigureAwait(true) explicitly to satisfy CA2007 (AnalysisMode=All).

using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.App.ViewModels;
using AgentSubscriptionTracker.Tests.TestSupport;

namespace AgentSubscriptionTracker.Tests.Tray;

public sealed class TrayViewModelBudgetTests
{
    private static readonly DateTimeOffset T0 = TrayTestData.T0;

    private static readonly RefreshPolicy ShortBudgetPolicy = new()
    {
        PerProviderTimeout = TimeSpan.FromMilliseconds(50),
    };

    private static ClaudeUsageSnapshot ClaudeOk(DateTimeOffset retrievedAt) =>
        TrayFixtures.LoadClaudeSnapshot("claude_snapshot_ok.json") with { RetrievedAt = retrievedAt };

    private static FakeCopilotQuotaService CopilotOkService() =>
        FakeCopilotQuotaService.Returning(
            TrayFixtures.LoadCopilotSnapshot("copilot_snapshot_ok.json") with { RetrievedAt = T0 });

    [Fact]
    public async Task BudgetElapsedKeepsLoadingAndPublishesWhenFetchLands()
    {
        var time = new TestTimeProvider(T0);
        var gate = new TaskCompletionSource<ClaudeUsageSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var claude = new FakeClaudeUsageService().EnqueueHandler(_ => gate.Task);
        var vm = new TrayViewModel(claude, CopilotOkService(), time, ShortBudgetPolicy);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        // Budget elapsed: the still-running fetch must not be misreported as Unavailable.
        Assert.Equal(ProviderDisplayState.Loading, vm.Claude.DisplayState);
        Assert.False(vm.Claude.HasData);
        Assert.False(vm.IsRefreshing);
        Assert.Equal(ProviderDisplayState.Ok, vm.Copilot.DisplayState);

        // The detached fetch lands later and is published as if it had finished in time.
        gate.SetResult(ClaudeOk(T0));
        await WaitUntilAsync(() => vm.Claude.HasData).ConfigureAwait(true);
        Assert.Equal(ProviderDisplayState.Ok, vm.Claude.DisplayState);
        Assert.Equal(4, vm.Claude.Bars.Count);
    }

    [Fact]
    public async Task BudgetElapsedDoesNotFlipPreviousDataToUnavailable()
    {
        var time = new TestTimeProvider(T0);
        var neverCompletes = new TaskCompletionSource<ClaudeUsageSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var claude = new FakeClaudeUsageService()
            .Enqueue(ClaudeOk(T0))
            .EnqueueHandler(_ => neverCompletes.Task);
        var vm = new TrayViewModel(claude, CopilotOkService(), time, ShortBudgetPolicy);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);
        Assert.Equal(ProviderDisplayState.Ok, vm.Claude.DisplayState);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        // Second fetch exceeded the budget: previous good data stays exactly as it was.
        Assert.Equal(2, claude.CallCount);
        Assert.Equal(ProviderDisplayState.Ok, vm.Claude.DisplayState);
        Assert.True(vm.Claude.HasData);
        Assert.False(vm.Claude.IsStale);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10).ConfigureAwait(true);
        }

        Assert.True(condition(), "condition not reached within the polling window");
    }
}
