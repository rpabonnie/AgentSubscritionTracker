using System.ComponentModel;
using AgentSubscriptionTracker.App.Models;
using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>
/// Root view-model bound by the callout window. Not thread-safe: the shell calls it on the
/// dispatcher thread. Refresh orchestration per SPEC-0003 §4.4: single-flight, per-provider
/// hover gating and timeout budget, fail-closed presentation, all timing via <see cref="TimeProvider"/>.
/// </summary>
public sealed class TrayViewModel : INotifyPropertyChanged
{
    private readonly IClaudeUsageService _claudeService;
    private readonly ICopilotQuotaService _copilotService;
    private readonly TimeProvider _timeProvider;
    private readonly RefreshPolicy _policy;

    private ClaudeUsageSnapshot? _lastClaudeSnapshot;
    private CopilotQuotaSnapshot? _lastCopilotSnapshot;
    private DateTimeOffset? _claudeAttemptCompletedUtc;
    private DateTimeOffset? _copilotAttemptCompletedUtc;
    private Task? _inFlight;

    private ProviderViewModel _claude;
    private ProviderViewModel _copilot;
    private bool _isRefreshing;

    public TrayViewModel(
        IClaudeUsageService claudeService,
        ICopilotQuotaService copilotService,
        TimeProvider timeProvider,
        RefreshPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(claudeService);
        ArgumentNullException.ThrowIfNull(copilotService);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _claudeService = claudeService;
        _copilotService = copilotService;
        _timeProvider = timeProvider;
        _policy = policy ?? RefreshPolicy.Default;
        _claude = ProviderViewModel.CreateEmpty("Claude");
        _copilot = ProviderViewModel.CreateEmpty("GitHub Copilot");
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Claude panel.</summary>
    public ProviderViewModel Claude => _claude;

    /// <summary>Copilot panel.</summary>
    public ProviderViewModel Copilot => _copilot;

    /// <summary>True while any provider call is outstanding.</summary>
    public bool IsRefreshing => _isRefreshing;

    /// <summary>Data-age footer text; recomputed on read via the injected <see cref="TimeProvider"/>.</summary>
    public string DataAgeText
    {
        get
        {
            DateTimeOffset? newest = null;
            if (_claude.HasData && _claude.RetrievedAtUtc is { } claudeAt)
            {
                newest = claudeAt;
            }

            if (_copilot.HasData && _copilot.RetrievedAtUtc is { } copilotAt
                && (newest is null || copilotAt > newest))
            {
                newest = copilotAt;
            }

            return UsageFormatting.FormatDataAge(newest, _timeProvider.GetUtcNow());
        }
    }

    /// <summary>Refresh orchestration (SPEC-0003 §4.4). Never throws except caller-token cancellation.</summary>
    public Task RequestRefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (_inFlight is { } inFlight)
        {
            return inFlight;
        }

        var now = _timeProvider.GetUtcNow();
        var refreshClaude = IsEligible(trigger, _claudeAttemptCompletedUtc, _policy.ClaudeMinInterval, now);
        var refreshCopilot = IsEligible(trigger, _copilotAttemptCompletedUtc, _policy.CopilotMinInterval, now);
        if (!refreshClaude && !refreshCopilot)
        {
            return Task.CompletedTask;
        }

        var task = RunRefreshAsync(refreshClaude, refreshCopilot, cancellationToken);
        _inFlight = task.IsCompleted ? null : task;
        return task;
    }

    private static bool IsEligible(
        RefreshTrigger trigger, DateTimeOffset? lastAttemptCompletedUtc, TimeSpan minInterval, DateTimeOffset now) =>
        trigger == RefreshTrigger.Manual
            || lastAttemptCompletedUtc is not { } lastAttempt
            || now - lastAttempt >= minInterval;

    private async Task RunRefreshAsync(bool refreshClaude, bool refreshCopilot, CancellationToken cancellationToken)
    {
        SetIsRefreshing(true);
        try
        {
            var work = new List<Task>(2);
            if (refreshClaude)
            {
                work.Add(RefreshClaudeAsync(cancellationToken));
            }

            if (refreshCopilot)
            {
                work.Add(RefreshCopilotAsync(cancellationToken));
            }

            await Task.WhenAll(work).ConfigureAwait(false);
        }
        finally
        {
            _inFlight = null;
            SetIsRefreshing(false);
            OnPropertyChanged(nameof(DataAgeText));
        }
    }

    private async Task RefreshClaudeAsync(CancellationToken callerToken)
    {
        // §4.4 step 5 (amended 2026-06-11): the budget bounds how long this refresh pass
        // waits, but never cancels the underlying fetch — a slow first fetch (OAuth refresh
        // round-trip, cold TLS) keeps running and is published when it lands.
        var fetch = _claudeService.GetUsageAsync(callerToken);
        var withinBudget = await WaitWithinBudgetAsync(fetch, callerToken).ConfigureAwait(false);
        if (withinBudget)
        {
            await PublishClaudeAsync(fetch, callerToken).ConfigureAwait(false);
            return;
        }

        // Budget elapsed: keep showing the current data, gate further hovers, and let the
        // in-flight fetch publish itself on completion (cancellation no longer applies).
        _claudeAttemptCompletedUtc = _timeProvider.GetUtcNow();
        _ = PublishClaudeAsync(fetch, CancellationToken.None);
    }

    private async Task PublishClaudeAsync(Task<ClaudeUsageSnapshot> fetch, CancellationToken callerToken)
    {
        ClaudeUsageSnapshot snapshot;
        try
        {
            snapshot = await fetch.ConfigureAwait(false);
            _lastClaudeSnapshot = snapshot;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            _claudeAttemptCompletedUtc = _timeProvider.GetUtcNow();
            throw;
        }
        catch (Exception)
        {
            // Fail closed (§4.4): unexpected service exception — keep cached numbers when
            // available, present Unavailable. Exception details are never surfaced.
            snapshot = _lastClaudeSnapshot is { } last
                ? last with { State = ClaudeProviderState.Unavailable, IsFromCache = true }
                : new ClaudeUsageSnapshot
                {
                    State = ClaudeProviderState.Unavailable,
                    RetrievedAt = _timeProvider.GetUtcNow(),
                };
        }

        // Success and failure both count: failures must not cause hover-hammering.
        _claudeAttemptCompletedUtc = _timeProvider.GetUtcNow();
        _claude = ProviderViewModel.ForClaude(snapshot, _timeProvider.GetUtcNow());
        OnPropertyChanged(nameof(Claude));
        OnPropertyChanged(nameof(DataAgeText));
    }

    private async Task RefreshCopilotAsync(CancellationToken callerToken)
    {
        // See RefreshClaudeAsync for the budget semantics.
        var fetch = _copilotService.GetSnapshotAsync(callerToken);
        var withinBudget = await WaitWithinBudgetAsync(fetch, callerToken).ConfigureAwait(false);
        if (withinBudget)
        {
            await PublishCopilotAsync(fetch, callerToken).ConfigureAwait(false);
            return;
        }

        _copilotAttemptCompletedUtc = _timeProvider.GetUtcNow();
        _ = PublishCopilotAsync(fetch, CancellationToken.None);
    }

    private async Task PublishCopilotAsync(Task<CopilotQuotaSnapshot> fetch, CancellationToken callerToken)
    {
        CopilotQuotaSnapshot snapshot;
        try
        {
            snapshot = await fetch.ConfigureAwait(false);
            _lastCopilotSnapshot = snapshot;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            _copilotAttemptCompletedUtc = _timeProvider.GetUtcNow();
            throw;
        }
        catch (Exception)
        {
            // Fail closed (§4.4); see PublishClaudeAsync.
            snapshot = _lastCopilotSnapshot is { } last
                ? last with { State = CopilotProviderState.Unavailable, IsFromCache = true }
                : new CopilotQuotaSnapshot
                {
                    State = CopilotProviderState.Unavailable,
                    RetrievedAt = _timeProvider.GetUtcNow(),
                };
        }

        _copilotAttemptCompletedUtc = _timeProvider.GetUtcNow();
        _copilot = ProviderViewModel.ForCopilot(snapshot, _timeProvider.GetUtcNow());
        OnPropertyChanged(nameof(Copilot));
        OnPropertyChanged(nameof(DataAgeText));
    }

    /// <summary>
    /// True when the fetch settled inside <see cref="RefreshPolicy.PerProviderTimeout"/>;
    /// false when the budget elapsed first. Throws only for caller cancellation.
    /// </summary>
    private async Task<bool> WaitWithinBudgetAsync(Task fetch, CancellationToken callerToken)
    {
        if (fetch.IsCompleted)
        {
            return true;
        }

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        var budget = Task.Delay(_policy.PerProviderTimeout, _timeProvider, budgetCts.Token);
        var first = await Task.WhenAny(fetch, budget).ConfigureAwait(false);
        if (first == fetch)
        {
            // Release the timer; the budget task's cancellation is expected and unobserved.
            await budgetCts.CancelAsync().ConfigureAwait(false);
            return true;
        }

        callerToken.ThrowIfCancellationRequested();
        return false;
    }

    private void SetIsRefreshing(bool value)
    {
        if (_isRefreshing == value)
        {
            return;
        }

        _isRefreshing = value;
        OnPropertyChanged(nameof(IsRefreshing));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
