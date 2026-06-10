// SPEC-0003 contract skeletons (TASK-008). Compile-only: these exist so the whole test
// project compiles; behavior is implemented by the SPEC-0003 code task and its tests are
// expected to stay red until then (members throw NotImplementedException).

using System.ComponentModel;
using AgentSubscriptionTracker.App.Models;
using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>What initiated a refresh request.</summary>
public enum RefreshTrigger
{
    /// <summary>Tooltip hover; honors per-provider minimum intervals.</summary>
    Hover,

    /// <summary>Explicit user action; bypasses minimum intervals.</summary>
    Manual,
}

/// <summary>Per-provider refresh pacing.</summary>
public sealed record RefreshPolicy
{
    /// <summary>Minimum interval between Claude service calls on hover.</summary>
    public TimeSpan ClaudeMinInterval { get; init; } = TimeSpan.FromSeconds(180);

    /// <summary>Minimum interval between Copilot service calls on hover.</summary>
    public TimeSpan CopilotMinInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-provider timeout for one refresh pass.</summary>
    public TimeSpan PerProviderTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Default policy values.</summary>
    public static RefreshPolicy Default { get; } = new();
}

/// <summary>Severity for a usage bar: &lt;75 / 75–90 / &gt;=90 percent used.</summary>
public enum UsageSeverity
{
    /// <summary>Less than 75 % used.</summary>
    Normal,

    /// <summary>75–90 % used.</summary>
    Warning,

    /// <summary>90 % or more used.</summary>
    Critical,
}

/// <summary>One rendered usage bar.</summary>
public sealed record UsageBarViewModel(
    string Label,
    double PercentUsed,
    bool IsUnlimited,
    string ValueText,
    string? ResetText,
    UsageSeverity Severity);

/// <summary>Presentation state for one provider panel.</summary>
public enum ProviderDisplayState
{
    /// <summary>No refresh attempted yet.</summary>
    Loading,

    /// <summary>Fresh data shown.</summary>
    Ok,

    /// <summary>User must sign in.</summary>
    SignedOut,

    /// <summary>Token expired/rejected; user action needed.</summary>
    AttentionRequired,

    /// <summary>Rate limited; retrying later.</summary>
    RateLimited,

    /// <summary>Provider temporarily unavailable.</summary>
    Unavailable,
}

/// <summary>Immutable presentation model for one provider. Skeleton until TASK-008.</summary>
public sealed class ProviderViewModel
{
    private ProviderViewModel()
    {
    }

    /// <summary>"Claude" or "GitHub Copilot".</summary>
    public string ProviderName { get; } = string.Empty;

    /// <summary>Presentation state.</summary>
    public ProviderDisplayState DisplayState { get; }

    /// <summary>State text for non-Ok states.</summary>
    public string? StateMessage { get; }

    /// <summary>Plan label ("Max", "Pro", ...).</summary>
    public string? PlanText { get; }

    /// <summary>True when at least one bar is shown.</summary>
    public bool HasData { get; }

    /// <summary>True when bars come from a cached (stale) snapshot.</summary>
    public bool IsStale { get; }

    /// <summary>Usage bars in display order.</summary>
    public IReadOnlyList<UsageBarViewModel> Bars { get; } = [];

    /// <summary>Footer line (extra usage / monthly reset).</summary>
    public string? FooterText { get; }

    /// <summary>UTC instant of the underlying snapshot, when any.</summary>
    public DateTimeOffset? RetrievedAtUtc { get; }

    /// <summary>Loading placeholder. Skeleton until TASK-008.</summary>
    public static ProviderViewModel CreateEmpty(string providerName) =>
        throw new NotImplementedException();

    /// <summary>Maps a Claude snapshot. Skeleton until TASK-008.</summary>
    public static ProviderViewModel ForClaude(ClaudeUsageSnapshot snapshot, DateTimeOffset utcNow) =>
        throw new NotImplementedException();

    /// <summary>Maps a Copilot snapshot. Skeleton until TASK-008.</summary>
    public static ProviderViewModel ForCopilot(CopilotQuotaSnapshot snapshot, DateTimeOffset utcNow) =>
        throw new NotImplementedException();
}

/// <summary>Tray tooltip root view-model. Skeleton until TASK-008.</summary>
public sealed class TrayViewModel : INotifyPropertyChanged
{
    /// <summary>Skeleton constructor; throws until SPEC-0003 is implemented.</summary>
    public TrayViewModel(
        IClaudeUsageService claudeService,
        ICopilotQuotaService copilotService,
        TimeProvider timeProvider,
        RefreshPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(claudeService);
        ArgumentNullException.ThrowIfNull(copilotService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged
    {
        // Skeleton accessors: no instance can exist while the constructor throws.
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>Claude panel.</summary>
    public ProviderViewModel Claude { get; } = null!;

    /// <summary>Copilot panel.</summary>
    public ProviderViewModel Copilot { get; } = null!;

    /// <summary>True while a refresh pass is running.</summary>
    public bool IsRefreshing { get; }

    /// <summary>Data-age line for the tooltip footer.</summary>
    public string DataAgeText { get; } = string.Empty;

    /// <summary>Refresh both providers per the policy. Skeleton until TASK-008.</summary>
    public Task RequestRefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken = default) =>
        // GetType() keeps this an instance member for the contract (no instance can exist yet).
        Task.FromException(new NotImplementedException(GetType().FullName));
}

/// <summary>Display-string helpers. Skeleton until TASK-008.</summary>
public static class UsageFormatting
{
    /// <summary>"Resets in 2 h 30 m" style countdown. Skeleton until TASK-008.</summary>
    public static string FormatCountdown(TimeSpan timeUntilReset) =>
        throw new NotImplementedException();

    /// <summary>"Updated 5 m ago" style data age. Skeleton until TASK-008.</summary>
    public static string FormatDataAge(DateTimeOffset? lastRetrievedUtc, DateTimeOffset utcNow) =>
        throw new NotImplementedException();

    /// <summary>"Resets Jul 1" style monthly reset. Skeleton until TASK-008.</summary>
    public static string FormatMonthlyReset(DateOnly resetDate) =>
        throw new NotImplementedException();

    /// <summary>"63% used" style percentage. Skeleton until TASK-008.</summary>
    public static string FormatPercentUsed(double percentUsed) =>
        throw new NotImplementedException();
}
