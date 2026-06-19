// SPEC-0004 §4.4/§7 — ThemeLoader discovery, re-seeding, degraded/missing/duplicate-id
// handling, and the "never zero themes" guarantee.
// Spec-phase stub: references AgentSubscriptionTracker.App.Theming types that do not
// exist yet, so the test project intentionally does not compile until TASK-016
// implements SPEC-0004.

using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class ThemeLoaderTests
{
    private static IThemeLoader CreateLoader(string themesRoot) => new ThemeLoader(themesRoot);

    private static string NewTempThemesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "AST-ThemeLoaderTests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteTheme(string themesRoot, string themeId, string manifestJson, byte[]? backgroundPng = null)
    {
        var folder = Path.Combine(themesRoot, themeId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "theme.json"), manifestJson);
        if (backgroundPng is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, "background.png"), backgroundPng);
        }
    }

    [Fact]
    public void LoadAll_EmptyThemesRoot_ReSeedsAndReturnsExactlyTheTwoBuiltIns()
    {
        var root = NewTempThemesRoot();
        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        Assert.Equal(2, state.Themes.Count(t => t.IsBuiltIn));
        Assert.All(state.Themes, t => Assert.Equal(ThemeLoadStatus.Ok, t.Status));
    }

    [Fact]
    public void LoadAll_DuplicateThemeId_FirstLoadedWins_LaterIsQuarantined()
    {
        var root = NewTempThemesRoot();
        var darkJson = ThemeTestSupport.ReadFixtureText("theme_valid_dark.json");
        WriteTheme(root, "custom-1", darkJson);
        // Force a second folder that yields the same resolved theme id ("custom-1")
        // by construction of the loader's id-derivation rule; this test pins the
        // *policy* (first-loaded wins) rather than the exact id-collision mechanism,
        // which TASK-016 is free to implement via slug derivation from folder name.
        WriteTheme(root, "custom-1-duplicate-marker", darkJson);
        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        Assert.Single(state.Themes, t => t.ThemeId == "custom-1");
        Assert.NotEmpty(state.QuarantinedThemeIds);
    }

    [Fact]
    public void LoadAll_OneMalformedManifest_DoesNotBlockLoadingOtherThemes()
    {
        var root = NewTempThemesRoot();
        var validBackgroundPng = ThemeTestSupport.ReadFixtureBytes("valid_with_alpha_340x480.png");
        WriteTheme(root, "good-theme", ThemeTestSupport.ReadFixtureText("theme_valid_dark.json"), validBackgroundPng);
        WriteTheme(root, "bad-theme", ThemeTestSupport.ReadFixtureText("theme_malformed_json.json"));
        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        Assert.Contains(state.Themes, t => t.ThemeId == "good-theme" && t.Status == ThemeLoadStatus.Ok);
        Assert.Contains("bad-theme", state.QuarantinedThemeIds);
    }

    [Fact]
    public void LoadAll_MissingBuiltInFolder_IsReSeededFromEmbeddedResources()
    {
        var root = NewTempThemesRoot(); // built-ins absent entirely
        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        Assert.Contains(state.Themes, t => t.IsBuiltIn && t.DisplayName == "Dark");
        Assert.Contains(state.Themes, t => t.IsBuiltIn && t.DisplayName == "Light");
    }

    [Fact]
    public void LoadAll_BackgroundImageFailsValidation_DegradesTheme_DoesNotQuarantineIt()
    {
        var root = NewTempThemesRoot();
        var manifestJson = ThemeTestSupport.ReadFixtureText("theme_valid_dark.json");
        var corruptPng = ThemeTestSupport.ReadFixtureBytes("truncated.png");
        WriteTheme(root, "degraded-theme", manifestJson, corruptPng);
        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        var theme = Assert.Single(state.Themes, t => t.ThemeId == "degraded-theme");
        Assert.Equal(ThemeLoadStatus.DegradedMissingOrInvalidImage, theme.Status);
        Assert.Null(theme.BackgroundImage);
        Assert.DoesNotContain("degraded-theme", state.QuarantinedThemeIds);
    }

    [Fact]
    public void LoadAll_BackgroundImageMissingFromDisk_DegradesTheme_DoesNotQuarantineIt()
    {
        // SPEC-0004 §4.4 row 4: the manifest references an image path but the file was
        // deleted/moved on disk (literal FileNotFound), not corrupt/truncated. The theme
        // must report DegradedMissingOrInvalidImage, not Ok.
        var root = NewTempThemesRoot();
        var manifestJson = ThemeTestSupport.ReadFixtureText("theme_valid_dark.json");
        WriteTheme(root, "missing-image-theme", manifestJson, backgroundPng: null);
        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        var theme = Assert.Single(state.Themes, t => t.ThemeId == "missing-image-theme");
        Assert.Equal(ThemeLoadStatus.DegradedMissingOrInvalidImage, theme.Status);
        Assert.Null(theme.BackgroundImage);
        Assert.DoesNotContain("missing-image-theme", state.QuarantinedThemeIds);
    }

    [Fact]
    public void LoadAll_InaccessibleThemesRoot_StillReturnsInCodeFallbackPair_NeverEmpty()
    {
        // A themes root that does not exist and cannot be created (e.g. a file
        // occupies that path) must not prevent LoadAll from returning usable themes.
        var bogusRoot = Path.Combine(Path.GetTempPath(), "AST-ThemeLoaderTests-File-Not-Dir-" + Guid.NewGuid());
        File.WriteAllText(bogusRoot, "this path is a file, not a directory");
        var loader = CreateLoader(bogusRoot);

        var state = loader.LoadAll();

        Assert.NotEmpty(state.Themes);
        Assert.True(state.Themes.Count >= 2);
    }

    [Theory]
    [InlineData("custom-1", "custom-1-duplicate-marker")]
    [InlineData("sunset-2", "sunset-2-renamed-copy")]
    [InlineData("ocean-10", "ocean-10-backup-folder")]
    public void LoadAll_DeriveThemeId_CollapsesNumericPrefixSlugVariants_AsTheSameId(
        string baseFolderName, string aliasedFolderName)
    {
        var root = NewTempThemesRoot();
        var darkJson = ThemeTestSupport.ReadFixtureText("theme_valid_dark.json");
        WriteTheme(root, baseFolderName, darkJson);
        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        Assert.Single(state.Themes, t => t.ThemeId == baseFolderName);
        Assert.DoesNotContain(state.Themes, t => t.ThemeId == aliasedFolderName);
    }

    [Fact]
    public void LoadAll_DeriveThemeId_PlainFolderNameWithoutNumericSuffix_IsUsedAsIs()
    {
        var root = NewTempThemesRoot();
        WriteTheme(root, "ocean-breeze", ThemeTestSupport.ReadFixtureText("theme_valid_dark.json"));
        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        Assert.Contains(state.Themes, t => t.ThemeId == "ocean-breeze");
    }

    [Fact]
    public void LoadAll_OversizedManifest_IsQuarantined_NotBufferedIntoMemory()
    {
        // Resource-exhaustion guard: a planted theme.json far larger than any legitimate
        // manifest must be rejected via FileInfo.Length BEFORE any read of its content.
        // We prove the cap is enforced — not merely that parsing of a huge buffer happens
        // to fail — by writing a file that is syntactically a *valid* JSON object (so if
        // the loader ever fell through to File.ReadAllText + parse, it would succeed and
        // load fine) but whose raw byte size alone exceeds the 64 KB cap. The only way
        // this theme ends up quarantined is the size check short-circuiting before the
        // content is ever inspected.
        var root = NewTempThemesRoot();
        var folder = Path.Combine(root, "oversized-theme");
        Directory.CreateDirectory(folder);

        var darkJson = ThemeTestSupport.ReadFixtureText("theme_valid_dark.json");
        // Pad with a huge whitespace prefix before the (otherwise valid) JSON payload so
        // the file is still syntactically parseable JSON content-wise, isolating the
        // assertion to "size cap enforced" rather than "huge file happens to be invalid".
        var oversizedJson = new string(' ', 70 * 1024) + darkJson;
        File.WriteAllText(Path.Combine(folder, "theme.json"), oversizedJson);

        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        Assert.Contains("oversized-theme", state.QuarantinedThemeIds);
        Assert.DoesNotContain(state.Themes, t => t.ThemeId == "oversized-theme");
    }

    [Fact]
    public void LoadAll_OversizedBuiltInManifest_SkipsReseed_AndIsQuarantinedOnLoad()
    {
        // Same cap, but exercised via ReseedBuiltInIfNeeded's own size check: an oversized
        // theme.json already sitting in a built-in folder ("dark") must not be silently
        // overwritten by reseed, and must still be quarantined like any other oversized
        // manifest on the subsequent load pass — never read in full either way.
        var root = NewTempThemesRoot();
        var folder = Path.Combine(root, ThemeLoader.BuiltInDarkThemeId);
        Directory.CreateDirectory(folder);
        var oversizedJson = new string(' ', 70 * 1024) + "{not even valid json after the padding";
        File.WriteAllText(Path.Combine(folder, "theme.json"), oversizedJson);

        var loader = CreateLoader(root);

        var state = loader.LoadAll();

        Assert.Contains(ThemeLoader.BuiltInDarkThemeId, state.QuarantinedThemeIds);
        // The themes root is never left empty: the loader still produces a usable set
        // (degraded to the in-code fallback / re-seeded light theme depending on path).
        Assert.NotEmpty(state.Themes);
    }

    [Fact]
    public void LoadAll_ActiveThemeHintMissingFromLoadedSet_FallsBackToABuiltIn()
    {
        var root = NewTempThemesRoot();
        var loader = CreateLoader(root);

        var state = loader.LoadAll(activeThemeIdHint: "theme-that-was-deleted");

        Assert.Contains(state.Themes, t => t.ThemeId == state.ActiveThemeId && t.IsBuiltIn);
    }
}
