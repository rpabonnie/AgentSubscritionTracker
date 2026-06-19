// SPEC-0004 §5.2 — hardcoded sample data for the theme editor's live preview. Mirrors
// realistic Claude/Copilot shapes via the real ProviderViewModel factories, but is never
// backed by a live service call — this is the only data source the editor's preview is
// ever allowed to use.

using AgentSubscriptionTracker.App.Models;
using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>Exposes <c>Claude</c>/<c>Copilot</c> the same way <see cref="TrayViewModel"/>
/// does, so the shared callout content's bindings work unmodified against either a real
/// view-model or this static sample, without TrayViewModel itself needing live services.</summary>
public sealed class SamplePreviewViewModel
{
    public required ProviderViewModel Claude { get; init; }
    public required ProviderViewModel Copilot { get; init; }
}

/// <summary>Builds the theme editor's static preview data (SPEC-0004 §5.2).</summary>
public static class SamplePreviewData
{
    public static SamplePreviewViewModel Build()
    {
        var now = DateTimeOffset.UtcNow;

        var claudeSnapshot = new ClaudeUsageSnapshot
        {
            State = ClaudeProviderState.Ok,
            RetrievedAt = now,
            IsFromCache = false,
            FiveHour = new ClaudeUsageBucket { PercentUsed = 42, ResetsAt = now.AddHours(3) },
            SevenDay = new ClaudeUsageBucket { PercentUsed = 81, ResetsAt = now.AddDays(2) },
            SevenDayOpus = new ClaudeUsageBucket { PercentUsed = 95, ResetsAt = now.AddDays(2) },
            SubscriptionType = "max",
        };

        var copilotSnapshot = new CopilotQuotaSnapshot
        {
            State = CopilotProviderState.Ok,
            Plan = "individual_pro",
            RetrievedAt = now,
            IsFromCache = false,
            QuotaResetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Chat = new CopilotQuotaBucket
            {
                Key = "chat",
                DisplayName = "Chat",
                Entitlement = 300,
                Remaining = 210,
                PercentRemaining = 70,
            },
            Completions = new CopilotQuotaBucket { Key = "completions", DisplayName = "Completions", Unlimited = true },
            PremiumInteractions = new CopilotQuotaBucket
            {
                Key = "premium_interactions",
                DisplayName = "Premium requests",
                Entitlement = 50,
                Remaining = 6,
                PercentRemaining = 12,
                OverageCount = 3,
                OveragePermitted = true,
            },
        };

        return new SamplePreviewViewModel
        {
            Claude = ProviderViewModel.ForClaude(claudeSnapshot, now),
            Copilot = ProviderViewModel.ForCopilot(copilotSnapshot, now),
        };
    }
}
