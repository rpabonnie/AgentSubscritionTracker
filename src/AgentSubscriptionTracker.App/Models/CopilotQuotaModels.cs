// SPEC-0002 contract skeletons (TASK-007). Compile-only: these exist so the whole test
// project compiles; behavior is implemented by the SPEC-0002 code task. Trivial pure
// model math is implemented inline; everything else throws NotImplementedException.

namespace AgentSubscriptionTracker.App.Models;

/// <summary>Connectivity/auth state of the Copilot quota provider. SPEC-0002.</summary>
public enum CopilotProviderState
{
    /// <summary>Quota data fetched (or served from a fresh cache) successfully.</summary>
    Ok,

    /// <summary>No token discovered or token rejected with 401.</summary>
    NotSignedIn,

    /// <summary>Token authenticated but not authorized (403).</summary>
    Forbidden,

    /// <summary>Endpoint returned 429; retry later.</summary>
    RateLimited,

    /// <summary>Network error, timeout, 5xx, or malformed response.</summary>
    Unavailable,
}

/// <summary>One Copilot quota bucket (chat / completions / premium interactions).</summary>
public sealed record CopilotQuotaBucket
{
    /// <summary>Wire key, e.g. "premium_interactions".</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable label, e.g. "Premium requests".</summary>
    public required string DisplayName { get; init; }

    /// <summary>Total entitlement for the period. Null when not reported.</summary>
    public int? Entitlement { get; init; }

    /// <summary>Remaining count. Null when not reported.</summary>
    public int? Remaining { get; init; }

    /// <summary>Percent remaining as reported by the API. Null when not reported.</summary>
    public double? PercentRemaining { get; init; }

    /// <summary>True when the bucket is unlimited (numeric fields are then meaningless).</summary>
    public bool Unlimited { get; init; }

    /// <summary>Overage requests consumed.</summary>
    public int OverageCount { get; init; }

    /// <summary>Whether overage is permitted.</summary>
    public bool OveragePermitted { get; init; }

    /// <summary>Entitlement - Remaining; null when unlimited or either input is null.</summary>
    public int? Used =>
        Unlimited || Entitlement is null || Remaining is null ? null : Entitlement - Remaining;
}

/// <summary>Immutable result of a Copilot quota poll (or of the cache).</summary>
public sealed record CopilotQuotaSnapshot
{
    /// <summary>Provider state for this poll.</summary>
    public required CopilotProviderState State { get; init; }

    /// <summary>Copilot plan, e.g. "individual_pro". Null when unknown.</summary>
    public string? Plan { get; init; }

    /// <summary>Monthly quota reset date. Null when unknown.</summary>
    public DateOnly? QuotaResetDate { get; init; }

    /// <summary>Chat bucket. Null when not reported.</summary>
    public CopilotQuotaBucket? Chat { get; init; }

    /// <summary>Code-completions bucket. Null when not reported.</summary>
    public CopilotQuotaBucket? Completions { get; init; }

    /// <summary>Premium-interactions bucket. Null when not reported.</summary>
    public CopilotQuotaBucket? PremiumInteractions { get; init; }

    /// <summary>UTC instant the underlying data was fetched.</summary>
    public DateTimeOffset RetrievedAt { get; init; }

    /// <summary>True when data comes from the cached previous successful poll.</summary>
    public bool IsFromCache { get; init; }

    /// <summary>User-facing status text for non-Ok states (token-free by contract).</summary>
    public string? StatusMessage { get; init; }

    /// <summary>Countdown to the reset moment (QuotaResetDate at 00:00 UTC), clamped to zero.</summary>
    public TimeSpan? GetTimeUntilReset(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (QuotaResetDate is not { } resetDate)
        {
            return null;
        }

        var resetMoment = new DateTimeOffset(resetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var remaining = resetMoment - timeProvider.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
