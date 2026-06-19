// SPEC-0004 §5.1/§4.4 — direct unit coverage for ThemePickerViewModel: ordering,
// duplicate-display-name disambiguation, long-name truncation, and the delete refusal
// rules for the active theme and built-ins (edge case #11).

using AgentSubscriptionTracker.App.Theming;
using AgentSubscriptionTracker.App.ViewModels;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class ThemePickerViewModelTests
{
    private static LoadedTheme MakeTheme(string id, string displayName, bool isBuiltIn = false) => new()
    {
        ThemeId = id,
        DisplayName = displayName,
        Manifest = ThemeTestSupport.ValidDarkManifest(),
        Status = ThemeLoadStatus.Ok,
        IsBuiltIn = isBuiltIn,
        BackgroundImage = null,
        FolderPath = Path.Combine(Path.GetTempPath(), "AST-ThemePickerViewModelTests-" + id),
    };

    private static ThemeRepositoryState MakeState(IReadOnlyList<LoadedTheme> themes, string activeThemeId) => new()
    {
        Themes = themes,
        ActiveThemeId = activeThemeId,
        QuarantinedThemeIds = [],
    };

    [Fact]
    public void Entries_ActiveThemeSortsFirst_ThenAlphabeticalByDisplayName()
    {
        var themes = new[]
        {
            MakeTheme("zeta", "Zeta"),
            MakeTheme("alpha", "Alpha"),
            MakeTheme("mid", "Mid", isBuiltIn: true),
        };
        var state = MakeState(themes, activeThemeId: "mid");

        var viewModel = new ThemePickerViewModel(state);

        Assert.Equal(["mid", "alpha", "zeta"], viewModel.Entries.Select(e => e.ThemeId));
        Assert.True(viewModel.Entries.Single(e => e.ThemeId == "mid").IsActive);
    }

    [Fact]
    public void Entries_DuplicateDisplayNames_AreDisambiguatedForDisplayOnly()
    {
        var themes = new[]
        {
            MakeTheme("custom-1", "Sunset"),
            MakeTheme("custom-2", "Sunset"),
        };
        var state = MakeState(themes, activeThemeId: "custom-1");

        var viewModel = new ThemePickerViewModel(state);

        var first = viewModel.Entries.Single(e => e.ThemeId == "custom-1");
        var second = viewModel.Entries.Single(e => e.ThemeId == "custom-2");
        Assert.Equal("Sunset", first.DisplayName);
        Assert.Equal("Sunset", second.DisplayName);
        Assert.NotEqual(first.DisambiguatedDisplayName, second.DisambiguatedDisplayName);
    }

    [Fact]
    public void Entries_LongDisplayName_IsEllipsisTruncatedForDisplay()
    {
        var longName = new string('A', 60);
        var themes = new[] { MakeTheme("long", longName) };
        var state = MakeState(themes, activeThemeId: "long");

        var viewModel = new ThemePickerViewModel(state);

        var entry = Assert.Single(viewModel.Entries);
        Assert.True(entry.TruncatedDisplayName.Length < longName.Length);
        Assert.EndsWith("…", entry.TruncatedDisplayName, StringComparison.Ordinal);
        Assert.Equal(longName, entry.DisambiguatedDisplayName);
    }

    [Fact]
    public void TryDelete_ActiveTheme_IsRefusedWithMessage_AndDoesNotCallStore()
    {
        var themes = new[] { MakeTheme("custom-1", "Custom") };
        var state = MakeState(themes, activeThemeId: "custom-1");
        var store = new RecordingThemeStore();
        var viewModel = new ThemePickerViewModel(state, store);

        var message = viewModel.TryDelete(viewModel.Entries.Single());

        Assert.NotNull(message);
        Assert.False(store.DeleteCalled);
    }

    [Fact]
    public void TryDelete_BuiltInTheme_IsRefusedWithMessage_AndDoesNotCallStore()
    {
        var themes = new[]
        {
            MakeTheme("dark", "Dark", isBuiltIn: true),
            MakeTheme("custom-1", "Custom"),
        };
        var state = MakeState(themes, activeThemeId: "custom-1");
        var store = new RecordingThemeStore();
        var viewModel = new ThemePickerViewModel(state, store);

        var message = viewModel.TryDelete(viewModel.Entries.Single(e => e.ThemeId == "dark"));

        Assert.NotNull(message);
        Assert.False(store.DeleteCalled);
    }

    [Fact]
    public void TryDelete_NonActiveNonBuiltInTheme_DeletesViaStore_ReturnsNull()
    {
        var themes = new[]
        {
            MakeTheme("dark", "Dark", isBuiltIn: true),
            MakeTheme("custom-1", "Custom"),
        };
        var state = MakeState(themes, activeThemeId: "dark");
        var store = new RecordingThemeStore();
        var viewModel = new ThemePickerViewModel(state, store);

        var message = viewModel.TryDelete(viewModel.Entries.Single(e => e.ThemeId == "custom-1"));

        Assert.Null(message);
        Assert.True(store.DeleteCalled);
        Assert.Equal("custom-1", store.DeletedThemeId);
    }

    private sealed class RecordingThemeStore : IThemeStore
    {
        public bool DeleteCalled { get; private set; }
        public string? DeletedThemeId { get; private set; }

        public bool IsBuiltIn(string themeId) => themeId is "dark" or "light";

        public string SaveAsNew(ThemeManifest manifest, byte[]? backgroundPngBytes) =>
            throw new NotSupportedException();

        public void Overwrite(string themeId, ThemeManifest manifest, byte[]? backgroundPngBytes) =>
            throw new NotSupportedException();

        public void Delete(string themeId)
        {
            DeleteCalled = true;
            DeletedThemeId = themeId;
        }
    }
}
