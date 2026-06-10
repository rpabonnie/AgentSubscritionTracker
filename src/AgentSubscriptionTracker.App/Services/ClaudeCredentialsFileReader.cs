using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;

namespace AgentSubscriptionTracker.App.Services;

/// <summary>Reads Claude Code OAuth credentials. Returns null when not signed in.</summary>
public interface IClaudeCredentialsReader
{
    /// <summary>
    /// Fresh read every call. Null when the file is missing, unparseable, lacks
    /// claudeAiOauth, or lacks a non-empty accessToken. Never throws.
    /// </summary>
    ClaudeOAuthCredentials? Read();
}

/// <summary>Parsed claudeAiOauth credentials. Held in memory only; ToString() is redacted.</summary>
public sealed record ClaudeOAuthCredentials
{
    /// <summary>OAuth access token. Never written to disk or diagnostic output.</summary>
    public required string AccessToken { get; init; }

    /// <summary>OAuth refresh token, when present. Never written to disk or diagnostic output.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Converted from the file's epoch-milliseconds value.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>"pro" / "max" when present.</summary>
    public string? SubscriptionType { get; init; }

    /// <summary>Redacted: never contains token values (CLAUDE.md Security Standards).</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"ClaudeOAuthCredentials {{ AccessToken = [REDACTED], RefreshToken = {(RefreshToken is null ? "(none)" : "[REDACTED]")}, ExpiresAt = {ExpiresAt:O}, SubscriptionType = {SubscriptionType ?? "(unknown)"} }}");
}

/// <summary>
/// Reads %USERPROFILE%\.claude\.credentials.json (read-only, shared; the file is never
/// written, moved, or locked — a running Claude Code may rotate it at any time).
/// </summary>
public sealed class ClaudeCredentialsFileReader : IClaudeCredentialsReader
{
    private readonly string _credentialsFilePath;

    /// <summary>Uses %USERPROFILE%\.claude\.credentials.json.</summary>
    public ClaudeCredentialsFileReader()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            ".credentials.json"))
    {
    }

    /// <summary>Test/override constructor with an explicit file path.</summary>
    public ClaudeCredentialsFileReader(string credentialsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialsFilePath);
        _credentialsFilePath = credentialsFilePath;
    }

    /// <inheritdoc />
    public ClaudeOAuthCredentials? Read()
    {
        try
        {
            using var stream = new FileStream(
                _credentialsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            return Parse(document.RootElement);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClaudeOAuthCredentials? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("claudeAiOauth", out var oauth) ||
            oauth.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var accessToken = ReadString(oauth, "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        return new ClaudeOAuthCredentials
        {
            AccessToken = accessToken,
            RefreshToken = ReadString(oauth, "refreshToken"),
            ExpiresAt = ReadEpochMilliseconds(oauth, "expiresAt"),
            SubscriptionType = ReadString(oauth, "subscriptionType"),
        };
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset ReadEpochMilliseconds(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var milliseconds) &&
            milliseconds >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() &&
            milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        // Missing/invalid expiry: treat as already expired so the refresh path decides.
        return DateTimeOffset.MinValue;
    }
}
