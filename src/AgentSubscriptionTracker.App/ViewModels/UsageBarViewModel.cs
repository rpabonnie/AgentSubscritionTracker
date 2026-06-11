namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>Severity bucket for bar coloring. SPEC-0003 §4.</summary>
public enum UsageSeverity
{
    /// <summary>Less than 75 % used.</summary>
    Normal,

    /// <summary>75–90 % used.</summary>
    Warning,

    /// <summary>90 % or more used.</summary>
    Critical,
}

/// <summary>One usage/quota progress bar. Immutable. SPEC-0003 §4.</summary>
public sealed record UsageBarViewModel(
    string Label,
    double PercentUsed,
    bool IsUnlimited,
    string ValueText,
    string? ResetText,
    UsageSeverity Severity);
