// SPEC-0002 §3 — Copilot token discovery chain. Read-only, memory-only: nothing is
// written, token values and file contents are never logged. Every step tolerates
// missing/unreadable/malformed sources and falls through to the next one.

using System.IO;
using System.Text.Json;

namespace AgentSubscriptionTracker.App.Services;

/// <summary>
/// Discovers a locally stored GitHub Copilot OAuth token using the research-verified
/// chain: Windows Credential Manager ("copilot-cli") → %LOCALAPPDATA%\github-copilot
/// apps.json/hosts.json → ~/.config/github-copilot apps.json/hosts.json →
/// ~/.copilot/config.json → gh CLI keyring ("gh:github.com:") → gh CLI hosts.yml
/// (SPEC-0002 §3 incl. the 2026-06-11 amendment). First non-empty token wins.
/// </summary>
public sealed class CopilotTokenProvider : ICopilotTokenProvider
{
    private const string CredentialServiceName = "copilot-cli";
    private const string GitHubKeyPrefix = "github.com";
    private const string OAuthTokenProperty = "oauth_token";

    // gh CLI keyring targets (go-keyring wincred format "service:user"; the active-account
    // entry has an empty user). Verified: the blob is the raw gh OAuth token.
    private static readonly string[] GhCredentialTargets = ["gh:github.com:", "gh:github.com"];

    private readonly ICopilotCredentialStore _credentialStore;
    private readonly string _localAppDataPath;
    private readonly string _userProfilePath;
    private readonly string _roamingAppDataPath;

    /// <summary>Creates the provider; path overrides default to the real user folders.</summary>
    public CopilotTokenProvider(
        ICopilotCredentialStore credentialStore,
        CopilotTokenProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(credentialStore);
        _credentialStore = credentialStore;
        _localAppDataPath = options?.LocalAppDataPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _userProfilePath = options?.UserProfilePath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _roamingAppDataPath = options?.RoamingAppDataPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    /// <inheritdoc />
    public Task<CopilotToken?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Discover());
    }

    private CopilotToken? Discover()
    {
        // 1. Windows Credential Manager, service "copilot-cli".
        if (ExtractTokenFromCredentialBlob(_credentialStore.ReadSecret(CredentialServiceName)) is { } secret)
        {
            return new CopilotToken(secret, CopilotTokenSource.CredentialManager);
        }

        // 2–3. %LOCALAPPDATA%\github-copilot\apps.json, then hosts.json.
        var localCopilotDir = Path.Combine(_localAppDataPath, "github-copilot");
        if (TryReadGitHubKeyedToken(Path.Combine(localCopilotDir, "apps.json")) is { } appsToken)
        {
            return new CopilotToken(appsToken, CopilotTokenSource.AppsJson);
        }

        if (TryReadGitHubKeyedToken(Path.Combine(localCopilotDir, "hosts.json")) is { } hostsToken)
        {
            return new CopilotToken(hostsToken, CopilotTokenSource.HostsJson);
        }

        // 4. ~/.config/github-copilot/apps.json, then hosts.json (older plugins).
        var userConfigDir = Path.Combine(_userProfilePath, ".config", "github-copilot");
        if (TryReadGitHubKeyedToken(Path.Combine(userConfigDir, "apps.json")) is { } userAppsToken)
        {
            return new CopilotToken(userAppsToken, CopilotTokenSource.UserConfigAppsJson);
        }

        if (TryReadGitHubKeyedToken(Path.Combine(userConfigDir, "hosts.json")) is { } userHostsToken)
        {
            return new CopilotToken(userHostsToken, CopilotTokenSource.UserConfigHostsJson);
        }

        // 5. ~/.copilot/config.json (Copilot CLI plaintext fallback).
        if (TryReadCliConfigToken(Path.Combine(_userProfilePath, ".copilot", "config.json")) is { } cliToken)
        {
            return new CopilotToken(cliToken, CopilotTokenSource.CopilotCliConfig);
        }

        // 6. gh CLI keyring entries in Windows Credential Manager (SPEC-0002 §3 step 6:
        //    Copilot CLI ≥ 2026 stores no plaintext token; the gh OAuth token is accepted
        //    by copilot_internal/user).
        foreach (var target in GhCredentialTargets)
        {
            if (ExtractTokenFromCredentialBlob(_credentialStore.ReadSecret(target)) is { } ghSecret)
            {
                return new CopilotToken(ghSecret, CopilotTokenSource.GhCliCredentialManager);
            }
        }

        // 7. %APPDATA%\GitHub CLI\hosts.yml (gh CLI plaintext fallback, no OS keyring).
        if (TryReadGhHostsYamlToken(Path.Combine(_roamingAppDataPath, "GitHub CLI", "hosts.yml")) is { } ghFileToken)
        {
            return new CopilotToken(ghFileToken, CopilotTokenSource.GhCliHostsFile);
        }

        return null;
    }

    /// <summary>
    /// Minimal hosts.yml scan (no YAML dependency): the first "oauth_token: &lt;value&gt;" line
    /// inside the "github.com:" host block. Any IO/format issue yields null.
    /// </summary>
    private static string? TryReadGhHostsYamlToken(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var insideGitHubHost = false;
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var isHostHeader = !char.IsWhiteSpace(line[0]) && line.TrimEnd().EndsWith(':');
                if (isHostHeader)
                {
                    insideGitHubHost = line.TrimEnd().TrimEnd(':').Trim() == GitHubKeyPrefix;
                    continue;
                }

                if (!insideGitHubHost)
                {
                    continue;
                }

                var trimmed = line.Trim();
                if (trimmed.StartsWith(OAuthTokenProperty + ":", StringComparison.Ordinal))
                {
                    var value = trimmed[(OAuthTokenProperty.Length + 1)..].Trim().Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// SPEC-0002 §3.1 — a JSON-object blob with a non-empty "oauth_token" yields that
    /// value; any other non-empty blob is used as the trimmed raw token.
    /// </summary>
    private static string? ExtractTokenFromCredentialBlob(string? blob)
    {
        if (string.IsNullOrWhiteSpace(blob))
        {
            return null;
        }

        var trimmed = blob.Trim();
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                ReadNonEmptyString(document.RootElement, OAuthTokenProperty) is { } token)
            {
                return token;
            }
        }
        catch (JsonException)
        {
            // Not JSON — treat the blob itself as the token.
        }

        return trimmed;
    }

    /// <summary>
    /// apps.json / hosts.json: a JSON object keyed by "github.com" (hosts) or
    /// "github.com:Iv1.&lt;appid&gt;" (apps); first "github.com"-prefixed entry with a
    /// non-empty oauth_token wins. An exact "github.com" key is preferred when present.
    /// </summary>
    private static string? TryReadGitHubKeyedToken(string path)
    {
        using var document = TryParseJsonFile(path);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? prefixMatch = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.StartsWith(GitHubKeyPrefix, StringComparison.Ordinal) ||
                property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (ReadNonEmptyString(property.Value, OAuthTokenProperty) is not { } token)
            {
                continue;
            }

            if (property.Name.Length == GitHubKeyPrefix.Length)
            {
                return token; // exact "github.com" key
            }

            prefixMatch ??= token;
        }

        return prefixMatch;
    }

    /// <summary>
    /// ~/.copilot/config.json: a top-level non-empty "oauth_token", or
    /// "github.com" → object → "oauth_token" (tolerant; first match wins).
    /// </summary>
    private static string? TryReadCliConfigToken(string path)
    {
        using var document = TryParseJsonFile(path);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (ReadNonEmptyString(document.RootElement, OAuthTokenProperty) is { } topLevelToken)
        {
            return topLevelToken;
        }

        if (document.RootElement.TryGetProperty(GitHubKeyPrefix, out var gitHubEntry) &&
            gitHubEntry.ValueKind == JsonValueKind.Object)
        {
            return ReadNonEmptyString(gitHubEntry, OAuthTokenProperty);
        }

        return null;
    }

    /// <summary>Reads + parses a JSON file; any IO/access/format failure yields null.</summary>
    private static JsonDocument? TryParseJsonFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadNonEmptyString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.GetString() is { } text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return null;
    }
}
