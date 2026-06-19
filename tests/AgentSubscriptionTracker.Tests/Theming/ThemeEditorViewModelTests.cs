// SPEC-0004 §5.2/§4.4 — direct unit coverage for ThemeEditorViewModel: Save routing
// (built-in/missing-source-folder -> SaveAsNew, existing non-built-in -> Overwrite),
// low-contrast computation, and background-image import validation.

using AgentSubscriptionTracker.App.Theming;
using AgentSubscriptionTracker.App.ViewModels;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class ThemeEditorViewModelTests
{
    private static ThemeEditorViewModel CreateViewModel(
        RecordingThemeStore store,
        string? sourceThemeId,
        bool sourceIsBuiltIn,
        string? sourceFolderPath,
        IThemeBackgroundImageValidator? imageValidator = null) =>
        new(
            ThemeTestSupport.ValidDarkManifest(),
            store,
            new ThemeFontResolver(),
            imageValidator ?? new ThemeBackgroundImageValidator(),
            sourceThemeId,
            sourceIsBuiltIn,
            sourceFolderPath);

    [Fact]
    public void Save_BuiltInSource_RoutesThroughSaveAsNew_NeverOverwrite()
    {
        var store = new RecordingThemeStore();
        var viewModel = CreateViewModel(store, sourceThemeId: "dark", sourceIsBuiltIn: true, sourceFolderPath: "C:\\does-not-matter");

        var themeId = viewModel.Save();

        Assert.True(store.SaveAsNewCalled);
        Assert.False(store.OverwriteCalled);
        Assert.Equal(store.SavedAsNewId, themeId);
    }

    [Fact]
    public void Save_NewTheme_NoSourceThemeId_RoutesThroughSaveAsNew()
    {
        var store = new RecordingThemeStore();
        var viewModel = CreateViewModel(store, sourceThemeId: null, sourceIsBuiltIn: false, sourceFolderPath: null);

        viewModel.Save();

        Assert.True(store.SaveAsNewCalled);
        Assert.False(store.OverwriteCalled);
    }

    [Fact]
    public void Save_ExistingNonBuiltInWithSourceFolderStillPresent_RoutesThroughOverwrite()
    {
        var store = new RecordingThemeStore();
        var tempFolder = Path.Combine(Path.GetTempPath(), "AST-ThemeEditorViewModelTests-" + Guid.NewGuid());
        Directory.CreateDirectory(tempFolder);
        try
        {
            var viewModel = CreateViewModel(store, sourceThemeId: "custom-1", sourceIsBuiltIn: false, sourceFolderPath: tempFolder);

            var themeId = viewModel.Save();

            Assert.True(store.OverwriteCalled);
            Assert.False(store.SaveAsNewCalled);
            Assert.Equal("custom-1", themeId);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public void Save_ExistingNonBuiltInWithSourceFolderDeleted_FallsThroughToSaveAsNew()
    {
        // §4.4 edge case #9: source file deleted/renamed mid-edit — operate on the
        // in-memory copy and prompt save-as (here: fall through automatically) rather
        // than fail or silently write to a stale path.
        var store = new RecordingThemeStore();
        var missingFolder = Path.Combine(Path.GetTempPath(), "AST-ThemeEditorViewModelTests-Missing-" + Guid.NewGuid());
        var viewModel = CreateViewModel(store, sourceThemeId: "custom-1", sourceIsBuiltIn: false, sourceFolderPath: missingFolder);

        viewModel.Save();

        Assert.True(store.SaveAsNewCalled);
        Assert.False(store.OverwriteCalled);
    }

    [Fact]
    public void LowContrastWarning_AcceptableContrast_IsNull()
    {
        var store = new RecordingThemeStore();
        var viewModel = CreateViewModel(store, sourceThemeId: null, sourceIsBuiltIn: false, sourceFolderPath: null);

        Assert.Null(viewModel.LowContrastWarning);
    }

    [Fact]
    public void LowContrastWarning_PrimaryTextNearBackgroundColor_IsNonNull()
    {
        var store = new RecordingThemeStore();
        var viewModel = CreateViewModel(store, sourceThemeId: null, sourceIsBuiltIn: false, sourceFolderPath: null);
        var brushes = viewModel.Brushes;
        viewModel.Brushes = brushes with { TextPrimaryBrush = brushes.CalloutBackgroundBrush };

        Assert.NotNull(viewModel.LowContrastWarning);
    }

    [Fact]
    public void TryImportBackgroundImage_FileDoesNotExist_SetsImportError_ReturnsFalse()
    {
        var store = new RecordingThemeStore();
        var viewModel = CreateViewModel(store, sourceThemeId: null, sourceIsBuiltIn: false, sourceFolderPath: null);
        var missingFile = Path.Combine(Path.GetTempPath(), "AST-does-not-exist-" + Guid.NewGuid() + ".png");

        var result = viewModel.TryImportBackgroundImage(missingFile);

        Assert.False(result);
        Assert.NotNull(viewModel.ImportError);
    }

    [Fact]
    public void TryImportBackgroundImage_NotAPng_SetsImportError_ReturnsFalse()
    {
        var store = new RecordingThemeStore();
        var viewModel = CreateViewModel(store, sourceThemeId: null, sourceIsBuiltIn: false, sourceFolderPath: null);
        var notPngFile = Path.Combine(Path.GetTempPath(), "AST-not-a-png-" + Guid.NewGuid() + ".png");
        File.WriteAllText(notPngFile, "this is not a png");
        try
        {
            var result = viewModel.TryImportBackgroundImage(notPngFile);

            Assert.False(result);
            Assert.NotNull(viewModel.ImportError);
        }
        finally
        {
            File.Delete(notPngFile);
        }
    }

    [Fact]
    public void TryImportBackgroundImage_ValidPng_StagesBytes_ClearsImportError_ReturnsTrue()
    {
        var store = new RecordingThemeStore();
        var viewModel = CreateViewModel(store, sourceThemeId: null, sourceIsBuiltIn: false, sourceFolderPath: null);
        var validPngBytes = ThemeTestSupport.ReadFixtureBytes("valid_with_alpha_340x480.png");
        var stagedFile = Path.Combine(Path.GetTempPath(), "AST-valid-import-" + Guid.NewGuid() + ".png");
        File.WriteAllBytes(stagedFile, validPngBytes);
        try
        {
            var result = viewModel.TryImportBackgroundImage(stagedFile);

            Assert.True(result);
            Assert.Null(viewModel.ImportError);
            Assert.Equal("background.png", viewModel.BackgroundImagePath);
        }
        finally
        {
            File.Delete(stagedFile);
        }
    }

    private sealed class RecordingThemeStore : IThemeStore
    {
        public bool SaveAsNewCalled { get; private set; }
        public bool OverwriteCalled { get; private set; }
        public string SavedAsNewId { get; private set; } = "saved-as-new";

        public bool IsBuiltIn(string themeId) => themeId is "dark" or "light";

        public string SaveAsNew(ThemeManifest manifest, byte[]? backgroundPngBytes)
        {
            SaveAsNewCalled = true;
            return SavedAsNewId;
        }

        public void Overwrite(string themeId, ThemeManifest manifest, byte[]? backgroundPngBytes) =>
            OverwriteCalled = true;

        public void Delete(string themeId) => throw new NotSupportedException();
    }
}
