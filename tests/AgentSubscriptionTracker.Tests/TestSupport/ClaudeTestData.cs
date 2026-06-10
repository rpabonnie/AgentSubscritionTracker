using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.Tests.TestSupport;

/// <summary>
/// Shared constants, fixture access, and factory helpers for the SPEC-0001 Claude usage
/// service tests. All token values are fakes (CLAUDE.md deny list: never real tokens).
/// </summary>
public static class ClaudeTestData
{
    /// <summary>Fixed "now" used across Claude tests: 2026-06-10T12:00:00Z.</summary>
    public static readonly DateTimeOffset TestNow = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Matches expiresAt 1781136000000 in credentials.valid.json (2026-06-11T00:00:00Z).</summary>
    public static readonly DateTimeOffset ValidTokenExpiresAt =
        DateTimeOffset.FromUnixTimeMilliseconds(1781136000000);

    /// <summary>Matches expiresAt 1780272000000 in credentials.expired.json (2026-06-01T00:00:00Z).</summary>
    public static readonly DateTimeOffset ExpiredTokenExpiresAt =
        DateTimeOffset.FromUnixTimeMilliseconds(1780272000000);

    public const string FakeAccessToken = "fake-access-token-for-tests-0001";
    public const string FakeExpiredAccessToken = "fake-expired-access-token-for-tests-0001";
    public const string FakeRefreshToken = "fake-refresh-token-for-tests-0001";
    public const string FakeRefreshedAccessToken = "fake-refreshed-access-token-for-tests-0002";
    public const string OAuthClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    public static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Claude", fileName);

    public static string ReadFixture(string fileName) => File.ReadAllText(FixturePath(fileName));

    /// <summary>Credentials equivalent to credentials.valid.json, valid at <see cref="TestNow"/>.</summary>
    public static ClaudeOAuthCredentials ValidCredentials() => new()
    {
        AccessToken = FakeAccessToken,
        RefreshToken = FakeRefreshToken,
        ExpiresAt = ValidTokenExpiresAt,
        SubscriptionType = "max",
    };

    /// <summary>Credentials whose ExpiresAt is in the past relative to <see cref="TestNow"/>.</summary>
    public static ClaudeOAuthCredentials ExpiredCredentials(bool withRefreshToken = true) => new()
    {
        AccessToken = FakeExpiredAccessToken,
        RefreshToken = withRefreshToken ? FakeRefreshToken : null,
        ExpiresAt = ExpiredTokenExpiresAt,
        SubscriptionType = "pro",
    };

    /// <summary>Options with zero retry delay so backoff paths run instantly in tests.</summary>
    public static ClaudeUsageServiceOptions FastOptions() => new()
    {
        RetryBaseDelay = TimeSpan.Zero,
    };

    public static ClaudeUsageService CreateService(
        IClaudeCredentialsReader credentialsReader,
        FakeHttpMessageHandler handler,
        TestTimeProvider timeProvider,
        ClaudeUsageServiceOptions? options = null) =>
        new(credentialsReader, handler, timeProvider, options ?? FastOptions());
}

/// <summary>Fake credentials source; counts reads so tests can assert fresh-per-poll behavior.</summary>
public sealed class FakeClaudeCredentialsReader : IClaudeCredentialsReader
{
    private readonly Func<ClaudeOAuthCredentials?> _factory;

    public FakeClaudeCredentialsReader(ClaudeOAuthCredentials? credentials)
        : this(() => credentials)
    {
    }

    public FakeClaudeCredentialsReader(Func<ClaudeOAuthCredentials?> factory)
    {
        _factory = factory;
    }

    public int ReadCount { get; private set; }

    public ClaudeOAuthCredentials? Read()
    {
        ReadCount++;
        return _factory();
    }
}
