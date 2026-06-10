namespace AgentSubscriptionTracker.App.Services;

/// <summary>Reports the signed-in user's Claude (Anthropic) subscription usage. SPEC-0001.</summary>
public interface IClaudeUsageService
{
    /// <summary>
    /// Never throws; all failures are expressed via <see cref="ClaudeUsageSnapshot.State"/>.
    /// The only allowed propagated exception is the caller's own cancellation.
    /// </summary>
    Task<ClaudeUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default);
}
