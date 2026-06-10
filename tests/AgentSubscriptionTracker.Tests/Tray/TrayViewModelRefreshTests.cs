// SPEC-0003 §4.4 — TrayViewModel refresh orchestration over service fakes.
// Spec-phase stubs: they reference view-model types that do not exist yet, so the
// test project intentionally does not compile until TASK-008 implements SPEC-0003.
//
// awaits use ConfigureAwait(true) explicitly to satisfy CA2007 (AnalysisMode=All);
// tests have no synchronization-context requirement either way.

using System.ComponentModel;
using AgentSubscriptionTracker.App.Models;
using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.App.ViewModels;
using AgentSubscriptionTracker.Tests.TestSupport;

namespace AgentSubscriptionTracker.Tests.Tray;

public sealed class TrayViewModelRefreshTests
{
    private static readonly DateTimeOffset T0 = TrayTestData.T0;

    private static ClaudeUsageSnapshot ClaudeOk(DateTimeOffset retrievedAt) =>
        TrayFixtures.LoadClaudeSnapshot("claude_snapshot_ok.json") with { RetrievedAt = retrievedAt };

    private static CopilotQuotaSnapshot CopilotOk(DateTimeOffset retrievedAt) =>
        TrayFixtures.LoadCopilotSnapshot("copilot_snapshot_ok.json") with { RetrievedAt = retrievedAt };

    private static TrayViewModel CreateViewModel(
        TestTimeProvider time,
        FakeClaudeUsageService claude,
        FakeCopilotQuotaService copilot,
        RefreshPolicy? policy = null) =>
        new(claude, copilot, time, policy);

    [Fact]
    public void InitialStateIsLoadingWithNoData()
    {
        var time = new TestTimeProvider(T0);
        var vm = CreateViewModel(time, new FakeClaudeUsageService(), new FakeCopilotQuotaService());

        Assert.Equal("Claude", vm.Claude.ProviderName);
        Assert.Equal("GitHub Copilot", vm.Copilot.ProviderName);
        Assert.Equal(ProviderDisplayState.Loading, vm.Claude.DisplayState);
        Assert.Equal(ProviderDisplayState.Loading, vm.Copilot.DisplayState);
        Assert.Equal("Loading…", vm.Claude.StateMessage);
        Assert.False(vm.Claude.HasData);
        Assert.False(vm.IsRefreshing);
        Assert.Equal("No data yet", vm.DataAgeText);
    }

    [Fact]
    public async Task ManualRefreshCallsBothServicesOnce()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        Assert.Equal(1, claude.CallCount);
        Assert.Equal(1, copilot.CallCount);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task ManualRefreshPublishesProvidersAndDataAge()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        Assert.True(vm.Claude.HasData);
        Assert.True(vm.Copilot.HasData);
        Assert.Equal(ProviderDisplayState.Ok, vm.Claude.DisplayState);
        Assert.Equal(ProviderDisplayState.Ok, vm.Copilot.DisplayState);
        Assert.Equal("Updated just now", vm.DataAgeText);
    }

    [Fact]
    public async Task HoverRefreshRunsBothWhenNeverAttempted()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Hover).ConfigureAwait(true);

        Assert.Equal(1, claude.CallCount);
        Assert.Equal(1, copilot.CallCount);
    }

    [Fact]
    public async Task HoverRefreshHonorsPerServiceMinIntervals()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(60));
        await vm.RequestRefreshAsync(RefreshTrigger.Hover).ConfigureAwait(true);

        // 60 s: past Copilot's 30 s minimum, inside Claude's 180 s minimum.
        Assert.Equal(1, claude.CallCount);
        Assert.Equal(2, copilot.CallCount);
    }

    [Fact]
    public async Task HoverRefreshSkipsBothInsideMinIntervals()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(10));
        await vm.RequestRefreshAsync(RefreshTrigger.Hover).ConfigureAwait(true);

        Assert.Equal(1, claude.CallCount);
        Assert.Equal(1, copilot.CallCount);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task HoverRefreshRunsClaudeAfterItsMinInterval()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(181));
        await vm.RequestRefreshAsync(RefreshTrigger.Hover).ConfigureAwait(true);

        Assert.Equal(2, claude.CallCount);
        Assert.Equal(2, copilot.CallCount);
    }

    [Fact]
    public async Task ManualRefreshBypassesMinIntervals()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(1));
        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        Assert.Equal(2, claude.CallCount);
        Assert.Equal(2, copilot.CallCount);
    }

    [Fact]
    public async Task OverlappingRefreshRequestsAreSingleFlight()
    {
        var time = new TestTimeProvider(T0);
        var gate = new TaskCompletionSource<ClaudeUsageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var claude = new FakeClaudeUsageService().EnqueueHandler(_ => gate.Task);
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        var first = vm.RequestRefreshAsync(RefreshTrigger.Manual);
        var second = vm.RequestRefreshAsync(RefreshTrigger.Manual);

        Assert.Equal(1, claude.CallCount);
        Assert.Equal(1, copilot.CallCount);
        Assert.True(vm.IsRefreshing);

        gate.SetResult(ClaudeOk(T0));
        await first.ConfigureAwait(true);
        await second.ConfigureAwait(true);

        Assert.False(vm.IsRefreshing);
        Assert.True(vm.Claude.HasData);
    }

    [Fact]
    public async Task ServiceExceptionBecomesUnavailablePresentation()
    {
        var time = new TestTimeProvider(T0);
        var claude = new FakeClaudeUsageService().EnqueueHandler(
            _ => Task.FromException<ClaudeUsageSnapshot>(new InvalidOperationException("boom")));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        Assert.Equal(ProviderDisplayState.Unavailable, vm.Claude.DisplayState);
        Assert.Equal("Claude usage temporarily unavailable", vm.Claude.StateMessage);
        Assert.False(vm.Claude.HasData);
        Assert.Equal(ProviderDisplayState.Ok, vm.Copilot.DisplayState);
    }

    [Fact]
    public async Task FailedRefreshKeepsPreviouslyPublishedData()
    {
        var time = new TestTimeProvider(T0);
        var claude = new FakeClaudeUsageService()
            .Enqueue(ClaudeOk(T0))
            .EnqueueHandler(_ => Task.FromException<ClaudeUsageSnapshot>(new InvalidOperationException("boom")));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);
        time.Advance(TimeSpan.FromSeconds(200));
        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        Assert.Equal(2, claude.CallCount);
        Assert.Equal(ProviderDisplayState.Unavailable, vm.Claude.DisplayState);
        Assert.True(vm.Claude.HasData);                 // cached bars survive the failure
        Assert.Equal(4, vm.Claude.Bars.Count);
        Assert.True(vm.Claude.IsStale);
        Assert.Equal("Updated 3 m ago", vm.DataAgeText); // age of the last good data, not the failed attempt
    }

    [Fact]
    public async Task AlreadyCancelledCallerTokenPropagatesWithoutServiceCalls()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => vm.RequestRefreshAsync(RefreshTrigger.Manual, cts.Token)).ConfigureAwait(true);

        Assert.Equal(0, claude.CallCount);
        Assert.Equal(0, copilot.CallCount);
    }

    [Fact]
    public async Task RefreshRaisesPropertyChangedForProvidersRefreshingAndDataAge()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(ClaudeOk(T0));
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0));
        var vm = CreateViewModel(time, claude, copilot);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        Assert.Contains(nameof(TrayViewModel.Claude), raised);
        Assert.Contains(nameof(TrayViewModel.Copilot), raised);
        Assert.Contains(nameof(TrayViewModel.IsRefreshing), raised);
        Assert.Contains(nameof(TrayViewModel.DataAgeText), raised);
    }

    [Fact]
    public async Task DataAgeUsesNewestProviderWithData()
    {
        var time = new TestTimeProvider(T0);
        var claude = FakeClaudeUsageService.Returning(
            TrayFixtures.LoadClaudeSnapshot("claude_snapshot_not_signed_in.json") with { RetrievedAt = T0 });
        var copilot = FakeCopilotQuotaService.Returning(CopilotOk(T0 - TimeSpan.FromSeconds(90)));
        var vm = CreateViewModel(time, claude, copilot);

        await vm.RequestRefreshAsync(RefreshTrigger.Manual).ConfigureAwait(true);

        // Claude has no data (NotSignedIn); the age reflects Copilot's snapshot.
        Assert.False(vm.Claude.HasData);
        Assert.Equal("Updated 1 m ago", vm.DataAgeText);
    }
}
