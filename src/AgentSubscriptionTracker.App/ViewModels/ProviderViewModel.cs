using System.Globalization;
using AgentSubscriptionTracker.App.Models;
using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>Connectivity presentation of one provider section (drives icon/color). SPEC-0003 §4.</summary>
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

/// <summary>Immutable presentation of one provider section. Rebuilt on every refresh. SPEC-0003 §4.1–§4.3.</summary>
public sealed class ProviderViewModel
{
    private ProviderViewModel(
        string providerName,
        ProviderDisplayState displayState,
        string? stateMessage,
        string? planText,
        bool isStale,
        IReadOnlyList<UsageBarViewModel> bars,
        string? footerText,
        DateTimeOffset? retrievedAtUtc)
    {
        ProviderName = providerName;
        DisplayState = displayState;
        StateMessage = stateMessage;
        PlanText = planText;
        IsStale = isStale;
        Bars = bars;
        FooterText = footerText;
        RetrievedAtUtc = retrievedAtUtc;
    }

    /// <summary>"Claude" or "GitHub Copilot".</summary>
    public string ProviderName { get; }

    /// <summary>Presentation state.</summary>
    public ProviderDisplayState DisplayState { get; }

    /// <summary>Null when <see cref="DisplayState"/> is Ok; otherwise the user-facing state message. Never contains a token.</summary>
    public string? StateMessage { get; }

    /// <summary>"Pro" / "Max" / "Individual" / "Business" / raw plan string; null when unknown.</summary>
    public string? PlanText { get; }

    /// <summary>True when at least one bar is shown.</summary>
    public bool HasData => Bars.Count > 0;

    /// <summary>True when the numbers shown come from a cached/stale snapshot.</summary>
    public bool IsStale { get; }

    /// <summary>Usage bars in display order.</summary>
    public IReadOnlyList<UsageBarViewModel> Bars { get; }

    /// <summary>Claude: extra-usage credits line. Copilot: monthly reset + overage line. Null when nothing to show.</summary>
    public string? FooterText { get; }

    /// <summary>UTC instant of the underlying snapshot; null only for <see cref="CreateEmpty"/>.</summary>
    public DateTimeOffset? RetrievedAtUtc { get; }

    /// <summary>Pre-first-refresh placeholder: Loading, "Loading…", no bars.</summary>
    public static ProviderViewModel CreateEmpty(string providerName)
    {
        ArgumentNullException.ThrowIfNull(providerName);
        return new(providerName, ProviderDisplayState.Loading, "Loading…", null, false, [], null, null);
    }

    /// <summary>Maps a Claude snapshot per SPEC-0003 §4.1.</summary>
    public static ProviderViewModel ForClaude(ClaudeUsageSnapshot snapshot, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var bars = new List<UsageBarViewModel>(4);
        AddClaudeBar(bars, "5-hour session", snapshot.FiveHour, utcNow);
        AddClaudeBar(bars, "Weekly (all models)", snapshot.SevenDay, utcNow);
        AddClaudeBar(bars, "Weekly (Opus)", snapshot.SevenDayOpus, utcNow);
        AddClaudeBar(bars, "Weekly (Sonnet)", snapshot.SevenDaySonnet, utcNow);

        var (state, message) = snapshot.State switch
        {
            ClaudeProviderState.Ok => (ProviderDisplayState.Ok, (string?)null),
            ClaudeProviderState.NotSignedIn => (ProviderDisplayState.SignedOut, "Sign in via Claude Code CLI"),
            ClaudeProviderState.TokenExpired =>
                (ProviderDisplayState.AttentionRequired, "Claude session expired — run /login in Claude Code"),
            ClaudeProviderState.RateLimited => (ProviderDisplayState.RateLimited, "Rate limited — will retry later"),
            _ => (ProviderDisplayState.Unavailable, "Claude usage temporarily unavailable"),
        };

        return new(
            "Claude",
            state,
            message,
            MapClaudePlan(snapshot.SubscriptionType),
            snapshot.IsFromCache,
            bars,
            BuildClaudeFooter(snapshot.ExtraUsage),
            snapshot.RetrievedAt);
    }

    /// <summary>Maps a Copilot snapshot per SPEC-0003 §4.1.</summary>
    public static ProviderViewModel ForCopilot(CopilotQuotaSnapshot snapshot, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = utcNow; // Copilot resets are date-based (footer); no countdown math is needed here.

        var bars = new List<UsageBarViewModel>(3);
        AddCopilotBar(bars, snapshot.PremiumInteractions);
        AddCopilotBar(bars, snapshot.Chat);
        AddCopilotBar(bars, snapshot.Completions);

        var state = snapshot.State switch
        {
            CopilotProviderState.Ok => ProviderDisplayState.Ok,
            CopilotProviderState.NotSignedIn => ProviderDisplayState.SignedOut,
            CopilotProviderState.Forbidden => ProviderDisplayState.AttentionRequired,
            CopilotProviderState.RateLimited => ProviderDisplayState.RateLimited,
            _ => ProviderDisplayState.Unavailable,
        };

        // A non-empty service StatusMessage wins (SPEC-0002 guarantees it is redacted and displayable).
        var message = state == ProviderDisplayState.Ok
            ? null
            : !string.IsNullOrEmpty(snapshot.StatusMessage)
                ? snapshot.StatusMessage
                : state switch
                {
                    ProviderDisplayState.SignedOut => "Sign in via GitHub Copilot CLI",
                    ProviderDisplayState.AttentionRequired => "Copilot token rejected — sign in again via Copilot CLI",
                    ProviderDisplayState.RateLimited => "Rate limited — will retry later",
                    _ => "Copilot quota temporarily unavailable",
                };

        return new(
            "GitHub Copilot",
            state,
            message,
            MapCopilotPlan(snapshot.Plan),
            snapshot.IsFromCache,
            bars,
            BuildCopilotFooter(snapshot),
            snapshot.RetrievedAt);
    }

    private static void AddClaudeBar(
        List<UsageBarViewModel> bars, string label, ClaudeUsageBucket? bucket, DateTimeOffset utcNow)
    {
        if (bucket is null)
        {
            return;
        }

        var percentUsed = Math.Clamp(bucket.PercentUsed, 0, 100);
        var resetText = bucket.TimeUntilReset(utcNow) is { } untilReset
            ? UsageFormatting.FormatCountdown(untilReset)
            : null;

        bars.Add(new(
            label,
            percentUsed,
            IsUnlimited: false,
            UsageFormatting.FormatPercentUsed(percentUsed),
            resetText,
            SeverityFor(percentUsed)));
    }

    private static void AddCopilotBar(List<UsageBarViewModel> bars, CopilotQuotaBucket? bucket)
    {
        if (bucket is null)
        {
            return;
        }

        if (bucket.Unlimited)
        {
            bars.Add(new(bucket.DisplayName, 0, IsUnlimited: true, "Unlimited", null, UsageSeverity.Normal));
            return;
        }

        double percentUsed;
        string valueText;
        if (bucket.PercentRemaining is { } percentRemaining)
        {
            percentUsed = Math.Clamp(100 - percentRemaining, 0, 100);
            valueText = bucket is { Remaining: { } remaining, Entitlement: { } entitlement }
                ? FormatCountsLeft(remaining, entitlement)
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(int)Math.Round(percentRemaining, MidpointRounding.AwayFromZero)}% left");
        }
        else if (bucket is { Remaining: { } remaining, Entitlement: { } entitlement } && entitlement > 0)
        {
            percentUsed = Math.Clamp(100 * (1 - (double)remaining / entitlement), 0, 100);
            valueText = FormatCountsLeft(remaining, entitlement);
        }
        else
        {
            return; // Nothing displayable.
        }

        bars.Add(new(bucket.DisplayName, percentUsed, IsUnlimited: false, valueText, null, SeverityFor(percentUsed)));
    }

    private static string FormatCountsLeft(int remaining, int entitlement) =>
        string.Create(CultureInfo.InvariantCulture, $"{remaining} of {entitlement} left");

    private static UsageSeverity SeverityFor(double percentUsed) => percentUsed switch
    {
        >= 90 => UsageSeverity.Critical,
        >= 75 => UsageSeverity.Warning,
        _ => UsageSeverity.Normal,
    };

    private static string? MapClaudePlan(string? subscriptionType)
    {
        if (string.IsNullOrWhiteSpace(subscriptionType))
        {
            return null;
        }

        if (string.Equals(subscriptionType, "pro", StringComparison.OrdinalIgnoreCase))
        {
            return "Pro";
        }

        if (string.Equals(subscriptionType, "max", StringComparison.OrdinalIgnoreCase))
        {
            return "Max";
        }

        return subscriptionType;
    }

    private static string? MapCopilotPlan(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
        {
            return null;
        }

        if (string.Equals(plan, "individual", StringComparison.OrdinalIgnoreCase))
        {
            return "Individual";
        }

        if (string.Equals(plan, "individual_pro", StringComparison.OrdinalIgnoreCase))
        {
            return "Pro";
        }

        if (string.Equals(plan, "business", StringComparison.OrdinalIgnoreCase))
        {
            return "Business";
        }

        return plan;
    }

    private static string? BuildClaudeFooter(ClaudeExtraUsage? extraUsage)
    {
        if (extraUsage is not { IsEnabled: true, UsedCreditsCents: { } usedCents })
        {
            return null;
        }

        var footer = "Extra usage: " + FormatMoney(usedCents, extraUsage.Currency);
        if (extraUsage.MonthlyLimitCents is { } limitCents)
        {
            footer += " of " + FormatMoney(limitCents, extraUsage.Currency);
        }

        return footer;
    }

    private static string FormatMoney(decimal cents, string? currency)
    {
        var amount = (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return currency is null || string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? "$" + amount
            : amount + " " + currency;
    }

    private static string? BuildCopilotFooter(CopilotQuotaSnapshot snapshot)
    {
        var parts = new List<string>(2);
        if (snapshot.QuotaResetDate is { } resetDate)
        {
            parts.Add(UsageFormatting.FormatMonthlyReset(resetDate));
        }

        if (snapshot.PremiumInteractions is { OverageCount: > 0 } premium)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{premium.OverageCount} overage used"));
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }
}
