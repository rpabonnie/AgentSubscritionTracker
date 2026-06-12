// SPEC-0002 public contracts (TASK-007): interfaces, enums, records, and options for the
// Copilot quota service. Implementations live in CopilotTokenProvider.cs,
// CopilotQuotaService.cs, and WindowsCredentialStore.cs.

using AgentSubscriptionTracker.App.Models;

namespace AgentSubscriptionTracker.App.Services;

/// <summary>Reports GitHub Copilot quota for the signed-in user. SPEC-0002.</summary>
public interface ICopilotQuotaService
{
    /// <summary>Never throws except for caller cancellation; failures map to snapshot states.</summary>
    Task<CopilotQuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>Where in the discovery chain a Copilot token was found.</summary>
public enum CopilotTokenSource
{
    /// <summary>Windows Credential Manager ("copilot-cli").</summary>
    CredentialManager,

    /// <summary>%LOCALAPPDATA%\github-copilot\apps.json.</summary>
    AppsJson,

    /// <summary>%LOCALAPPDATA%\github-copilot\hosts.json.</summary>
    HostsJson,

    /// <summary>~/.config/github-copilot/apps.json.</summary>
    UserConfigAppsJson,

    /// <summary>~/.config/github-copilot/hosts.json.</summary>
    UserConfigHostsJson,

    /// <summary>~/.copilot/config.json (Copilot CLI).</summary>
    CopilotCliConfig,

    /// <summary>Windows Credential Manager, gh CLI keyring targets ("gh:github.com:"). SPEC-0002 §3 step 6.</summary>
    GhCliCredentialManager,

    /// <summary>%APPDATA%\GitHub CLI\hosts.yml (gh CLI plaintext fallback). SPEC-0002 §3 step 7.</summary>
    GhCliHostsFile,
}

/// <summary>A discovered Copilot OAuth token and its source.</summary>
public sealed record CopilotToken(string Value, CopilotTokenSource Source)
{
    /// <summary>Redacted: never contains the token value (CLAUDE.md Security Standards).</summary>
    public override string ToString() =>
        $"CopilotToken {{ Value = [REDACTED], Source = {Source} }}";
}

/// <summary>Discovers a Copilot token from local credential stores/files.</summary>
public interface ICopilotTokenProvider
{
    /// <summary>Null when nothing in the discovery chain yields a usable token.</summary>
    Task<CopilotToken?> GetTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Abstraction over Windows Credential Manager (CredRead) so tests can fake it.</summary>
public interface ICopilotCredentialStore
{
    /// <summary>Returns the stored secret for the service name, or null when absent.</summary>
    string? ReadSecret(string serviceName);
}

/// <summary>Path overrides for the token discovery chain (tests use temp directories).</summary>
public sealed record CopilotTokenProviderOptions
{
    /// <summary>Override for %LOCALAPPDATA%. Null = real folder.</summary>
    public string? LocalAppDataPath { get; init; }

    /// <summary>Override for %USERPROFILE%. Null = real folder.</summary>
    public string? UserProfilePath { get; init; }

    /// <summary>Override for %APPDATA% (gh CLI hosts.yml fallback). Null = real folder.</summary>
    public string? RoamingAppDataPath { get; init; }
}

/// <summary>Tuning knobs for <see cref="CopilotQuotaService"/>.</summary>
public sealed record CopilotQuotaServiceOptions
{
    /// <summary>API base address; only api.github.com is allowlisted.</summary>
    public Uri BaseAddress { get; init; } = new("https://api.github.com/");

    /// <summary>HTTP timeout (kept at or below 10 s per CLAUDE.md).</summary>
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Minimum interval between polls that reach the network.</summary>
    public TimeSpan MinRefreshInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Additional attempts after the first, for transient failures only.</summary>
    public int MaxRetries { get; init; } = 2;
}
