// SPEC-0004 §5.1 — backs ThemePickerPopup: active-theme-first-then-alphabetical ordering,
// duplicate-display-name disambiguation, long-name truncation, and the "Add new theme" entry.

using System.Globalization;
using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>One row in the theme picker popup.</summary>
public sealed class ThemeListEntryViewModel
{
    public ThemeListEntryViewModel(LoadedTheme theme, string disambiguatedDisplayName, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        DisambiguatedDisplayName = disambiguatedDisplayName;
        IsActive = isActive;
    }

    public LoadedTheme Theme { get; }

    public string ThemeId => Theme.ThemeId;

    /// <summary>Raw manifest name, never merged/altered (§4.4 edge case #12 — ids stay the
    /// unique selection key; duplicate display names are permitted).</summary>
    public string DisplayName => Theme.DisplayName;

    /// <summary>Disambiguated for visual display only (e.g. " (2)" suffix) when another
    /// loaded theme shares the same raw <see cref="DisplayName"/>.</summary>
    public string DisambiguatedDisplayName { get; }

    /// <summary>Ellipsis-truncated (§4.4 edge case #19); the full name remains available via
    /// <see cref="DisambiguatedDisplayName"/> for a tooltip binding.</summary>
    public string TruncatedDisplayName => Truncate(DisambiguatedDisplayName, 28);

    public bool IsActive { get; }

    public bool IsBuiltIn => Theme.IsBuiltIn;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
}

/// <summary>Backs the non-activating theme picker popup (SPEC-0004 §5.1). Pure
/// presentation/ordering logic over an already-loaded <see cref="ThemeRepositoryState"/>;
/// does not itself perform any I/O except via the injected <see cref="IThemeStore"/> for
/// delete (§4.4 edge case #11).</summary>
public sealed class ThemePickerViewModel
{
    private readonly IThemeStore? _store;

    public ThemePickerViewModel(ThemeRepositoryState state)
        : this(state, store: null)
    {
    }

    public ThemePickerViewModel(ThemeRepositoryState state, IThemeStore? store)
    {
        ArgumentNullException.ThrowIfNull(state);
        _store = store;
        Entries = BuildEntries(state);
    }

    /// <summary>Active theme first, then alphabetical by display name (§5.1).</summary>
    public IReadOnlyList<ThemeListEntryViewModel> Entries { get; }

    /// <summary>Attempts to delete <paramref name="entry"/>'s theme. Refuses — without
    /// touching disk — and returns a non-null message for: the active theme (§4.4 edge
    /// case #11, "deleting the active theme is rejected ... unless another theme is
    /// selected first") and built-in themes (read-only, §4.4 edge case #13's read-only
    /// rule extends to delete). On success returns null and the caller is responsible for
    /// refreshing the picker from a fresh <c>LoadAll</c>.</summary>
    public string? TryDelete(ThemeListEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsActive)
        {
            return "The active theme can't be deleted. Switch to another theme first.";
        }

        if (entry.IsBuiltIn)
        {
            return "Built-in themes can't be deleted.";
        }

        if (_store is null)
        {
            return "This theme can't be deleted right now.";
        }

        _store.Delete(entry.ThemeId);
        return null;
    }

    private static List<ThemeListEntryViewModel> BuildEntries(ThemeRepositoryState state)
    {
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var theme in state.Themes)
        {
            nameCounts[theme.DisplayName] = nameCounts.GetValueOrDefault(theme.DisplayName) + 1;
        }

        var nameOccurrence = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<ThemeListEntryViewModel>(state.Themes.Count);

        var ordered = state.Themes
            .OrderBy(t => t.ThemeId == state.ActiveThemeId ? 0 : 1)
            .ThenBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ThemeId, StringComparer.Ordinal);

        foreach (var theme in ordered)
        {
            var disambiguated = theme.DisplayName;
            if (nameCounts[theme.DisplayName] > 1)
            {
                var occurrence = nameOccurrence.GetValueOrDefault(theme.DisplayName) + 1;
                nameOccurrence[theme.DisplayName] = occurrence;
                disambiguated = string.Create(
                    CultureInfo.InvariantCulture, $"{theme.DisplayName} ({occurrence})");
            }

            entries.Add(new ThemeListEntryViewModel(theme, disambiguated, theme.ThemeId == state.ActiveThemeId));
        }

        return entries;
    }
}
