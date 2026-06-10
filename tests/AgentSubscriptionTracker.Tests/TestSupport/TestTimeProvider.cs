namespace AgentSubscriptionTracker.Tests.TestSupport;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> for tests (CLAUDE.md: inject TimeProvider,
/// no DateTime.Now in logic). Time only moves when a test calls <see cref="Advance"/>
/// or <see cref="SetUtcNow"/>.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset startUtc)
    {
        _utcNow = startUtc;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan by) => _utcNow += by;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
