namespace AgentSubscriptionTracker.App.Services;

/// <summary>Connectivity/auth state of the Claude usage provider. SPEC-0001 §3.</summary>
public enum ClaudeProviderState
{
    /// <summary>Usage data fetched (or served from a fresh cache) successfully.</summary>
    Ok,

    /// <summary>Credentials file missing/unparseable or no access token. "Sign in via Claude Code".</summary>
    NotSignedIn,

    /// <summary>Access token expired (or rejected with 401) and refresh failed. "Re-login in Claude Code".</summary>
    TokenExpired,

    /// <summary>Endpoint returned 429; retry later (see <see cref="ClaudeUsageSnapshot.RetryAfter"/>).</summary>
    RateLimited,

    /// <summary>Network error, timeout, 403, 5xx, or malformed response.</summary>
    Unavailable,
}

/// <summary>One rate-limit window. Absolute counts are not exposed by the API.</summary>
public sealed record ClaudeUsageBucket
{
    private readonly double _percentUsed;

    /// <summary>API "utilization", clamped to [0, 100].</summary>
    public required double PercentUsed
    {
        get => _percentUsed;
        init => _percentUsed = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 100);
    }

    /// <summary>100 - <see cref="PercentUsed"/> (therefore also within [0, 100]).</summary>
    public double PercentRemaining => 100 - _percentUsed;

    /// <summary>API "resets_at" (UTC). Null when the API omits it.</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>
    /// Countdown to reset. Null when <see cref="ResetsAt"/> is null; never negative
    /// (clamped to <see cref="TimeSpan.Zero"/> when the reset moment has passed).
    /// </summary>
    public TimeSpan? TimeUntilReset(DateTimeOffset utcNow)
    {
        if (ResetsAt is not { } resetsAt)
        {
            return null;
        }

        var remaining = resetsAt - utcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

/// <summary>extra_usage block. Every subfield is optional/version-dependent.</summary>
public sealed record ClaudeExtraUsage
{
    /// <summary>API "is_enabled"; defaults to false when absent.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>API "used_credits", in cents. Null when absent.</summary>
    public decimal? UsedCreditsCents { get; init; }

    /// <summary>API "monthly_limit", in cents. Null when absent.</summary>
    public decimal? MonthlyLimitCents { get; init; }

    /// <summary>API "currency" (e.g. "USD"). Null when absent.</summary>
    public string? Currency { get; init; }
}

/// <summary>Immutable result of a poll (or of the cache). Never contains token text.</summary>
public sealed record ClaudeUsageSnapshot
{
    /// <summary>Provider state for this poll.</summary>
    public required ClaudeProviderState State { get; init; }

    /// <summary>UTC instant the underlying data was fetched (cache keeps the original instant).</summary>
    public required DateTimeOffset RetrievedAt { get; init; }

    /// <summary>True when bucket data comes from the cached previous successful poll.</summary>
    public bool IsFromCache { get; init; }

    /// <summary>Current 5-hour session window. Null when not reported.</summary>
    public ClaudeUsageBucket? FiveHour { get; init; }

    /// <summary>Weekly limit, all models. Null when not reported.</summary>
    public ClaudeUsageBucket? SevenDay { get; init; }

    /// <summary>Null = no Opus-specific limit reported (e.g. unlimited / not applicable).</summary>
    public ClaudeUsageBucket? SevenDayOpus { get; init; }

    /// <summary>Null = no Sonnet-specific limit reported.</summary>
    public ClaudeUsageBucket? SevenDaySonnet { get; init; }

    /// <summary>Pay-per-use extra usage block. Null when the API omits it.</summary>
    public ClaudeExtraUsage? ExtraUsage { get; init; }

    /// <summary>"pro"/"max" from the credentials file, when known.</summary>
    public string? SubscriptionType { get; init; }

    /// <summary>Populated only when <see cref="State"/> is <see cref="ClaudeProviderState.RateLimited"/>.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>utcNow - <see cref="RetrievedAt"/>, clamped to be never negative.</summary>
    public TimeSpan DataAge(DateTimeOffset utcNow)
    {
        var age = utcNow - RetrievedAt;
        return age > TimeSpan.Zero ? age : TimeSpan.Zero;
    }
}

/// <summary>Tuning knobs; defaults match docs/CLAUDE_USAGE_API_RESEARCH.md.</summary>
public sealed record ClaudeUsageServiceOptions
{
    /// <summary>Usage endpoint (allowlisted host api.anthropic.com).</summary>
    public Uri UsageEndpoint { get; init; } = new("https://api.anthropic.com/api/oauth/usage");

    /// <summary>OAuth refresh endpoints, tried in order until one succeeds.</summary>
    public IReadOnlyList<Uri> TokenRefreshEndpoints { get; init; } =
    [
        new("https://platform.claude.com/v1/oauth/token"),
        new("https://console.anthropic.com/v1/oauth/token"),
    ];

    /// <summary>Claude Code public OAuth client id (not a secret).</summary>
    public string OAuthClientId { get; init; } = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    /// <summary>Must be of the form "claude-code/&lt;version&gt;"; required to avoid persistent 429s.</summary>
    public string UserAgent { get; init; } = "claude-code/2.0.14";

    /// <summary>Minimum interval between polls that reach the network.</summary>
    public TimeSpan MinPollInterval { get; init; } = TimeSpan.FromSeconds(180);

    /// <summary>HTTP timeout (kept at or below 10 s per CLAUDE.md).</summary>
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Additional attempts after the first, for transient (5xx/network) failures only.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Base for exponential backoff + jitter. Tests set <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);
}
