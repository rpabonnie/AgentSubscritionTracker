// SPEC-0004 §5.1/§5.2/§5.3 — converts a LoadedTheme's manifest into a ResourceDictionary of
// the DynamicResource keys CalloutWindow.xaml (and the shared preview content) already
// consume: the 7 brush keys, "ok"/"warn"/"critical" severity brushes, and per-role fonts.
// Used both for the live application-wide swap (App.xaml.cs) and the editor's
// preview-scoped dictionary (never Application.Current.Resources) — same code path, two
// different target dictionaries, per SPEC-0004 §5.2.

using System.Collections;
using System.Windows;
using System.Windows.Media;
using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.App.Theming;

/// <summary>Builds/refreshes a <see cref="ResourceDictionary"/> of theme-driven
/// DynamicResource keys from a <see cref="LoadedTheme"/>. Stateless; never throws (an
/// unparsable color falls back to a neutral gray rather than throwing — manifests
/// reaching this point have already passed strict parse-time hex validation).</summary>
public static class ThemeResourceApplier
{
    /// <summary>Builds a new dictionary with every key the callout content binds to,
    /// resolved from <paramref name="theme"/>'s manifest. Background image (if any) is
    /// exposed under <c>CalloutBackgroundImage</c> as a nullable <see cref="ImageSource"/>
    /// (used by tests/consumers that need the raw source) and pre-composed into
    /// <c>CalloutBackgroundPaint</c> — the actual brush the Border's Background binds to:
    /// an <see cref="ImageBrush"/> over the image when one loaded, else the plain
    /// <c>CalloutBackgroundBrush</c> solid color.</summary>
    public static ResourceDictionary BuildResourceDictionary(LoadedTheme theme, IThemeFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(fontResolver);

        var manifest = theme.Manifest;
        var backgroundBrush = BrushFromHex(manifest.Brushes.CalloutBackgroundBrush);
        var backgroundImage = theme.Status == ThemeLoadStatus.Ok ? theme.BackgroundImage : null;
        var dictionary = new ResourceDictionary
        {
            ["CalloutBackgroundBrush"] = backgroundBrush,
            ["CalloutBackgroundPaint"] = BuildBackgroundPaint(backgroundImage, backgroundBrush),
            ["CalloutBorderBrush"] = BrushFromHex(manifest.Brushes.CalloutBorderBrush),
            ["TextPrimaryBrush"] = BrushFromHex(manifest.Brushes.TextPrimaryBrush),
            ["TextSecondaryBrush"] = BrushFromHex(manifest.Brushes.TextSecondaryBrush),
            ["BarTrackBrush"] = BrushFromHex(manifest.Brushes.BarTrackBrush),
            ["SeparatorBrush"] = BrushFromHex(manifest.Brushes.SeparatorBrush),
            ["AccentBrush"] = BrushFromHex(manifest.Brushes.AccentBrush),
            ["ok"] = BrushFromHex(manifest.SeverityBands.Ok),
            ["warn"] = BrushFromHex(manifest.SeverityBands.Warn),
            ["critical"] = BrushFromHex(manifest.SeverityBands.Critical),
            ["CalloutBackgroundFallbackBrush"] = BrushFromHex(manifest.Background.FallbackColor),
            ["CalloutBackgroundImage"] = backgroundImage,
            ["HeaderFontFamily"] = fontResolver.Resolve(manifest.Fonts.Header).Family,
            ["HeaderFontSize"] = fontResolver.Resolve(manifest.Fonts.Header).Size,
            ["HeaderFontWeight"] = fontResolver.Resolve(manifest.Fonts.Header).Weight,
            ["BodyFontFamily"] = fontResolver.Resolve(manifest.Fonts.Body).Family,
            ["BodyFontSize"] = fontResolver.Resolve(manifest.Fonts.Body).Size,
            ["BodyFontWeight"] = fontResolver.Resolve(manifest.Fonts.Body).Weight,
            ["FooterFontFamily"] = fontResolver.Resolve(manifest.Fonts.Footer).Family,
            ["FooterFontSize"] = fontResolver.Resolve(manifest.Fonts.Footer).Size,
            ["FooterFontWeight"] = fontResolver.Resolve(manifest.Fonts.Footer).Weight,
        };

        return dictionary;
    }

    /// <summary>Idempotent swap (§4.4 edge case #17 — rapid theme switching): replaces the
    /// entire merged-dictionary contents of <paramref name="target"/> with a single
    /// dictionary built from <paramref name="theme"/>. The last call always wins; there is
    /// no partial/incremental update that could race.</summary>
    public static void Apply(ResourceDictionary target, LoadedTheme theme, IThemeFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(target);

        var built = BuildResourceDictionary(theme, fontResolver);
        target.MergedDictionaries.Clear();
        target.MergedDictionaries.Add(built);
    }

    /// <summary>SPEC-0004 §5.2 — preview-scoped direct-write path (versus the app-wide
    /// merged <see cref="Apply"/>). Writes every built key straight onto
    /// <paramref name="target"/> via the <see cref="ResourceDictionary"/> indexer instead of
    /// through MergedDictionaries. A merged-dictionary Clear+Add swap does not reliably
    /// re-resolve DynamicResource references already realized inside templates/styles (the
    /// live preview would stop updating mid-edit); a direct keyed write raises a precise
    /// per-key resource-changed notification, and the direct entry outranks any merged child
    /// at this scope, so every consumer re-resolves. Used by the editor's live preview and
    /// each theme-picker row's live preview; never <c>Application.Current.Resources</c>.</summary>
    public static void ApplyDirect(ResourceDictionary target, LoadedTheme theme, IThemeFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(target);

        var built = BuildResourceDictionary(theme, fontResolver);
        foreach (DictionaryEntry entry in built)
        {
            // Write every key, including the possibly-null CalloutBackgroundImage, so each
            // raises a per-key DynamicResource invalidation at the preview scope.
            target[entry.Key] = entry.Value;
        }
    }

    private static Brush BuildBackgroundPaint(ImageSource? backgroundImage, Brush fallback)
    {
        if (backgroundImage is null)
        {
            return fallback;
        }

        var brush = new ImageBrush(backgroundImage) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
            // Falls through to the neutral-gray fallback below.
        }

        return Brushes.Gray;
    }
}
