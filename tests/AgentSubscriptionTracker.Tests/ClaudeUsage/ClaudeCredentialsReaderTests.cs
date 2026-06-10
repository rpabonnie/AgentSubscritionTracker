using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.Tests.TestSupport;

namespace AgentSubscriptionTracker.Tests.ClaudeUsage;

/// <summary>
/// SPEC-0001 §4.1 step 2 / §3 — token discovery from %USERPROFILE%\.claude\.credentials.json
/// (here: golden-file fixtures with fake tokens). The reader is read-only, never throws,
/// and returns null for every "not signed in" shape.
/// </summary>
public sealed class ClaudeCredentialsReaderTests
{
    [Fact]
    public void Read_ValidCredentialsFile_ParsesClaudeAiOauthSection()
    {
        // Arrange
        var reader = new ClaudeCredentialsFileReader(ClaudeTestData.FixturePath("credentials.valid.json"));

        // Act
        var credentials = reader.Read();

        // Assert
        Assert.NotNull(credentials);
        Assert.Equal(ClaudeTestData.FakeAccessToken, credentials.AccessToken);
        Assert.Equal(ClaudeTestData.FakeRefreshToken, credentials.RefreshToken);
        Assert.Equal("max", credentials.SubscriptionType);
    }

    [Fact]
    public void Read_ValidCredentialsFile_ConvertsExpiresAtFromEpochMilliseconds()
    {
        // Arrange — fixture has "expiresAt": 1781136000000 (epoch ms) = 2026-06-11T00:00:00Z.
        var reader = new ClaudeCredentialsFileReader(ClaudeTestData.FixturePath("credentials.valid.json"));

        // Act
        var credentials = reader.Read();

        // Assert
        Assert.NotNull(credentials);
        Assert.Equal(ClaudeTestData.ValidTokenExpiresAt, credentials.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, credentials.ExpiresAt.Offset); // UTC, not local time
    }

    [Fact]
    public void Read_MissingFile_ReturnsNullWithoutThrowing()
    {
        // Arrange — research doc: file absent on machines without Claude Code sign-in.
        var reader = new ClaudeCredentialsFileReader(
            ClaudeTestData.FixturePath("credentials.does-not-exist.json"));

        // Act
        var credentials = reader.Read();

        // Assert
        Assert.Null(credentials);
    }

    [Fact]
    public void Read_MalformedJson_ReturnsNullWithoutThrowing()
    {
        // Arrange
        var reader = new ClaudeCredentialsFileReader(ClaudeTestData.FixturePath("credentials.malformed.json"));

        // Act
        var credentials = reader.Read();

        // Assert
        Assert.Null(credentials);
    }

    [Fact]
    public void Read_MissingClaudeAiOauthSection_ReturnsNull()
    {
        // Arrange
        var reader = new ClaudeCredentialsFileReader(
            ClaudeTestData.FixturePath("credentials.missing-oauth.json"));

        // Act
        var credentials = reader.Read();

        // Assert
        Assert.Null(credentials);
    }

    [Fact]
    public void Read_MissingAccessToken_ReturnsNull()
    {
        // Arrange — claudeAiOauth exists but has no accessToken value.
        var reader = new ClaudeCredentialsFileReader(
            ClaudeTestData.FixturePath("credentials.no-access-token.json"));

        // Act
        var credentials = reader.Read();

        // Assert
        Assert.Null(credentials);
    }

    [Fact]
    public void CredentialsToString_NeverContainsTokenValues()
    {
        // Arrange — Security Standards: redact any *token* value in diagnostic output.
        var credentials = ClaudeTestData.ValidCredentials();

        // Act
        var text = credentials.ToString();

        // Assert
        Assert.DoesNotContain(ClaudeTestData.FakeAccessToken, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ClaudeTestData.FakeRefreshToken, text, StringComparison.OrdinalIgnoreCase);
    }
}
