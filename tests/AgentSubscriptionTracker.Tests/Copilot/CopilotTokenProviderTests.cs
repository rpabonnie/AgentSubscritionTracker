// SPEC-0002 §3 — token discovery chain tests for CopilotTokenProvider.
// All file access goes through CopilotTokenProviderOptions path overrides into a
// temp directory; Credential Manager is faked. No real credential store is ever read.
// Fixture tokens are fake by policy: "fake-token-for-tests*".

using AgentSubscriptionTracker.App.Services;

namespace AgentSubscriptionTracker.Tests.Copilot;

public sealed class CopilotTokenProviderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeCopilotCredentialStore _credentialStore = new();

    private string LocalAppData => Path.Combine(_temp.Root, "local");

    private string UserProfile => Path.Combine(_temp.Root, "profile");

    private string RoamingAppData => Path.Combine(_temp.Root, "roaming");

    public void Dispose() => _temp.Dispose();

    private CopilotTokenProvider CreateProvider() =>
        new(_credentialStore, new CopilotTokenProviderOptions
        {
            LocalAppDataPath = LocalAppData,
            UserProfilePath = UserProfile,
            RoamingAppDataPath = RoamingAppData,
        });

    [Fact]
    public async Task CredentialManagerServiceCopilotCliTakesPrecedenceOverFiles()
    {
        // Arrange — credential present AND files present; the store must win.
        _credentialStore.Secrets["copilot-cli"] = "fake-token-for-tests-credman";
        _temp.WriteFile(Path.Combine("local", "github-copilot", "apps.json"), CopilotFixtures.Read("apps.json"));
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-credman", token.Value);
        Assert.Equal(CopilotTokenSource.CredentialManager, token.Source);
        Assert.Contains("copilot-cli", _credentialStore.ReadServiceNames);
    }

    [Fact]
    public async Task CredentialManagerJsonBlobYieldsOauthTokenProperty()
    {
        // Arrange — some stores persist a JSON blob rather than the bare token.
        _credentialStore.Secrets["copilot-cli"] =
            """{ "user": "octocat", "oauth_token": "fake-token-for-tests-credman-json" }""";
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-credman-json", token.Value);
        Assert.Equal(CopilotTokenSource.CredentialManager, token.Source);
    }

    [Fact]
    public async Task AppsJsonInLocalAppDataIsSecondInChain()
    {
        // Arrange — no credential; apps.json AND hosts.json exist; apps.json wins.
        _temp.WriteFile(Path.Combine("local", "github-copilot", "apps.json"), CopilotFixtures.Read("apps.json"));
        _temp.WriteFile(Path.Combine("local", "github-copilot", "hosts.json"), CopilotFixtures.Read("hosts.json"));
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-apps", token.Value);
        Assert.Equal(CopilotTokenSource.AppsJson, token.Source);
    }

    [Fact]
    public async Task HostsJsonInLocalAppDataUsedWhenAppsJsonAbsent()
    {
        // Arrange
        _temp.WriteFile(Path.Combine("local", "github-copilot", "hosts.json"), CopilotFixtures.Read("hosts.json"));
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-hosts", token.Value);
        Assert.Equal(CopilotTokenSource.HostsJson, token.Source);
    }

    [Fact]
    public async Task UserConfigAppsJsonUsedWhenLocalAppDataEmpty()
    {
        // Arrange — older plugins write under ~/.config/github-copilot/.
        _temp.WriteFile(Path.Combine("profile", ".config", "github-copilot", "apps.json"), CopilotFixtures.Read("apps.json"));
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-apps", token.Value);
        Assert.Equal(CopilotTokenSource.UserConfigAppsJson, token.Source);
    }

    [Fact]
    public async Task UserConfigHostsJsonUsedWhenUserConfigAppsJsonAbsent()
    {
        // Arrange
        _temp.WriteFile(Path.Combine("profile", ".config", "github-copilot", "hosts.json"), CopilotFixtures.Read("hosts.json"));
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-hosts", token.Value);
        Assert.Equal(CopilotTokenSource.UserConfigHostsJson, token.Source);
    }

    [Fact]
    public async Task CopilotCliConfigJsonIsLastResort()
    {
        // Arrange — ~/.copilot/config.json plaintext fallback.
        _temp.WriteFile(Path.Combine("profile", ".copilot", "config.json"), CopilotFixtures.Read("cli_config.json"));
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-cli", token.Value);
        Assert.Equal(CopilotTokenSource.CopilotCliConfig, token.Source);
    }

    [Fact]
    public async Task EmptyDiscoveryChainReturnsNull()
    {
        // Arrange — nothing in the store, no files (research: this machine's real state).
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public async Task GhCliKeyringUsedWhenCopilotStoresEmpty()
    {
        // Arrange — SPEC-0002 §3 step 6: Copilot CLI ≥ 2026 leaves steps 1–5 empty; the gh CLI
        // keyring entry ("gh:github.com:", go-keyring service:user format) holds the raw token.
        _credentialStore.Secrets["gh:github.com:"] = "fake-token-for-tests-gh-keyring";
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-gh-keyring", token.Value);
        Assert.Equal(CopilotTokenSource.GhCliCredentialManager, token.Source);
        Assert.Contains("gh:github.com:", _credentialStore.ReadServiceNames);
    }

    [Fact]
    public async Task CopilotCliStoreTakesPrecedenceOverGhCliKeyring()
    {
        // Arrange — a dedicated Copilot credential must outrank the broader gh token.
        _credentialStore.Secrets["copilot-cli"] = "fake-token-for-tests-credman";
        _credentialStore.Secrets["gh:github.com:"] = "fake-token-for-tests-gh-keyring";
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-credman", token.Value);
        Assert.Equal(CopilotTokenSource.CredentialManager, token.Source);
    }

    [Fact]
    public async Task GhHostsYamlIsFinalFallback()
    {
        // Arrange — SPEC-0002 §3 step 7: keyring-less gh stores oauth_token in hosts.yml.
        _temp.WriteFile(
            Path.Combine("roaming", "GitHub CLI", "hosts.yml"),
            """
            github.com:
                users:
                    octocat:
                        oauth_token: fake-token-for-tests-gh-hosts-yml
                git_protocol: https
                oauth_token: fake-token-for-tests-gh-hosts-yml
                user: octocat
            """);
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-gh-hosts-yml", token.Value);
        Assert.Equal(CopilotTokenSource.GhCliHostsFile, token.Source);
    }

    [Fact]
    public async Task GhHostsYamlIgnoresOtherHostsBlocks()
    {
        // Arrange — tokens under a non-github.com host must not be picked up.
        _temp.WriteFile(
            Path.Combine("roaming", "GitHub CLI", "hosts.yml"),
            """
            ghe.example.com:
                oauth_token: fake-token-for-tests-wrong-host
                user: octocat
            """);
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public async Task MalformedAppsJsonIsSkippedAndChainContinues()
    {
        // Arrange — corrupt apps.json must not throw; chain proceeds to hosts.json.
        _temp.WriteFile(Path.Combine("local", "github-copilot", "apps.json"), "{ not valid json !!");
        _temp.WriteFile(Path.Combine("local", "github-copilot", "hosts.json"), CopilotFixtures.Read("hosts.json"));
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-hosts", token.Value);
        Assert.Equal(CopilotTokenSource.HostsJson, token.Source);
    }

    [Fact]
    public async Task EmptyOrWhitespaceTokenValuesAreSkipped()
    {
        // Arrange — hosts.json with blank oauth_token must not satisfy the chain.
        _temp.WriteFile(
            Path.Combine("local", "github-copilot", "hosts.json"),
            """{ "github.com": { "user": "octocat", "oauth_token": "   " } }""");
        _temp.WriteFile(Path.Combine("profile", ".copilot", "config.json"), CopilotFixtures.Read("cli_config.json"));
        var provider = CreateProvider();

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("fake-token-for-tests-cli", token.Value);
        Assert.Equal(CopilotTokenSource.CopilotCliConfig, token.Source);
    }
}
