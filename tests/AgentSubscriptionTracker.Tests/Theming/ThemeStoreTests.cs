// SPEC-0004 §4 — ThemeStore filesystem persistence: Save-As/fork (incl. unique-id
// collision handling and display-name slugification), in-place Overwrite (non-built-ins
// only, built-in id rejected), and Delete (non-built-ins only, built-in id rejected).
// Added per QA-0004 finding (medium): ThemeStore previously had zero unit tests.

using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class ThemeStoreTests
{
    private static string NewTempThemesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "AST-ThemeStoreTests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void SaveAsNew_WritesManifestAndImage_UnderASlugifiedThemeId()
    {
        var root = NewTempThemesRoot();
        var store = new ThemeStore(root);
        var manifest = ThemeTestSupport.ValidDarkManifest() with { Name = "My Custom Theme!" };

        var themeId = store.SaveAsNew(manifest, [1, 2, 3, 4]);

        Assert.Equal("my-custom-theme", themeId);
        var folder = Path.Combine(root, themeId);
        Assert.True(File.Exists(Path.Combine(folder, "theme.json")));
        Assert.True(File.Exists(Path.Combine(folder, "background.png")));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(Path.Combine(folder, "background.png")));
    }

    [Fact]
    public void SaveAsNew_NullBackgroundBytes_WritesManifestOnly_NoBackgroundFile()
    {
        var root = NewTempThemesRoot();
        var store = new ThemeStore(root);
        var manifest = ThemeTestSupport.ValidLightManifestNoImage();

        var themeId = store.SaveAsNew(manifest, null);

        var folder = Path.Combine(root, themeId);
        Assert.True(File.Exists(Path.Combine(folder, "theme.json")));
        Assert.False(File.Exists(Path.Combine(folder, "background.png")));
    }

    [Fact]
    public void SaveAsNew_ManifestRoundTrips_ViaThemeManifestSerializer()
    {
        var root = NewTempThemesRoot();
        var store = new ThemeStore(root);
        var manifest = ThemeTestSupport.ValidDarkManifest();

        var themeId = store.SaveAsNew(manifest, null);

        var json = File.ReadAllText(Path.Combine(root, themeId, "theme.json"));
        var result = new ThemeManifestSerializer().TryParse(json);
        Assert.Null(result.Error);
        Assert.Equal(manifest, result.Manifest);
    }

    [Fact]
    public void SaveAsNew_DuplicateDisplayName_ProducesUniqueDisambiguatedThemeId()
    {
        var root = NewTempThemesRoot();
        var store = new ThemeStore(root);
        var manifest = ThemeTestSupport.ValidDarkManifest() with { Name = "Sunset" };

        var firstId = store.SaveAsNew(manifest, null);
        var secondId = store.SaveAsNew(manifest, null);
        var thirdId = store.SaveAsNew(manifest, null);

        Assert.Equal("sunset", firstId);
        Assert.Equal("sunset-2", secondId);
        Assert.Equal("sunset-3", thirdId);
        Assert.True(Directory.Exists(Path.Combine(root, firstId)));
        Assert.True(Directory.Exists(Path.Combine(root, secondId)));
        Assert.True(Directory.Exists(Path.Combine(root, thirdId)));
    }

    [Theory]
    [InlineData("  Weird   Spacing  ", "weird-spacing")]
    [InlineData("Already-Slugged", "already-slugged")]
    [InlineData("!!!", "theme")]
    [InlineData("", "theme")]
    [InlineData("Émoji 🎨 Theme", "émoji-theme")]
    public void SaveAsNew_Slugify_NormalizesDisplayNameIntoFilesystemSafeId(string name, string expectedId)
    {
        var root = NewTempThemesRoot();
        var store = new ThemeStore(root);
        var manifest = ThemeTestSupport.ValidDarkManifest() with { Name = name };

        var themeId = store.SaveAsNew(manifest, null);

        Assert.Equal(expectedId, themeId);
    }

    [Fact]
    public void IsBuiltIn_ReturnsTrueOnlyForTheTwoShippedBuiltInIds()
    {
        var store = new ThemeStore(NewTempThemesRoot());

        Assert.True(store.IsBuiltIn(ThemeLoader.BuiltInDarkThemeId));
        Assert.True(store.IsBuiltIn(ThemeLoader.BuiltInLightThemeId));
        Assert.False(store.IsBuiltIn("some-custom-theme"));
    }

    [Fact]
    public void Overwrite_NonBuiltInTheme_ReplacesManifestAndImageInPlace()
    {
        var root = NewTempThemesRoot();
        var store = new ThemeStore(root);
        var original = ThemeTestSupport.ValidDarkManifest() with { Name = "Forked" };
        var themeId = store.SaveAsNew(original, [9, 9, 9]);

        var updated = original with { Name = "Forked Updated" };
        store.Overwrite(themeId, updated, [1]);

        var folder = Path.Combine(root, themeId);
        var json = File.ReadAllText(Path.Combine(folder, "theme.json"));
        var parsed = new ThemeManifestSerializer().TryParse(json);
        Assert.Equal("Forked Updated", parsed.Manifest!.Name);
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(folder, "background.png")));
    }

    [Fact]
    public void Overwrite_BuiltInThemeId_ThrowsInvalidOperationException()
    {
        var store = new ThemeStore(NewTempThemesRoot());
        var manifest = ThemeTestSupport.ValidDarkManifest();

        Assert.Throws<InvalidOperationException>(
            () => store.Overwrite(ThemeLoader.BuiltInDarkThemeId, manifest, null));
    }

    [Fact]
    public void Delete_NonBuiltInTheme_RemovesItsFolder()
    {
        var root = NewTempThemesRoot();
        var store = new ThemeStore(root);
        var manifest = ThemeTestSupport.ValidDarkManifest() with { Name = "Custom Forked Theme" };
        var themeId = store.SaveAsNew(manifest, null);
        Assert.True(Directory.Exists(Path.Combine(root, themeId)));

        store.Delete(themeId);

        Assert.False(Directory.Exists(Path.Combine(root, themeId)));
    }

    [Fact]
    public void Delete_NonExistentThemeId_DoesNotThrow()
    {
        var store = new ThemeStore(NewTempThemesRoot());

        store.Delete("never-existed");
    }

    [Fact]
    public void Delete_BuiltInThemeId_ThrowsInvalidOperationException()
    {
        var store = new ThemeStore(NewTempThemesRoot());

        Assert.Throws<InvalidOperationException>(() => store.Delete(ThemeLoader.BuiltInLightThemeId));
    }
}
