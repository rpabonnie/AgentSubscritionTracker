namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>What initiated a refresh request. SPEC-0003 §4.</summary>
public enum RefreshTrigger
{
    /// <summary>Tray mouse-hover: honors per-service minimum intervals.</summary>
    Hover,

    /// <summary>Context-menu "Refresh now": bypasses the view-model gate (services may still serve cache).</summary>
    Manual,
}

/// <summary>UI-side refresh gating. Defaults mirror the service-side minimums.</summary>
public sealed record RefreshPolicy
{
    /// <summary>Minimum interval between Claude service calls on hover (SPEC-0001 MinPollInterval).</summary>
    public TimeSpan ClaudeMinInterval { get; init; } = TimeSpan.FromSeconds(180);

    /// <summary>Minimum interval between Copilot service calls on hover (SPEC-0002 MinRefreshInterval).</summary>
    public TimeSpan CopilotMinInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-provider hover budget; a provider exceeding it keeps its previous data (CLAUDE.md).</summary>
    public TimeSpan PerProviderTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Default policy values.</summary>
    public static RefreshPolicy Default { get; } = new();
}
