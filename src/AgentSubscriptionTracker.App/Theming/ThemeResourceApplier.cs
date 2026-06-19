// SPEC-0004 §5.1/§5.2/§5.3 — converts a LoadedTheme's manifest into a ResourceDictionary of
// the DynamicResource keys CalloutWindow.xaml (and the shared preview content) already
// consume: the 7 brush keys, "ok"/"warn"/"critical" severity brushes, and per-role fonts.
// Used both for the live application-wide swap (App.xaml.cs) and the editor's
// preview-scoped dictionary (never Application.Current.Resources) — same code path, two
// different target dictionaries, per SPEC-0004 §5.2.

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
    /// — null means "fallback color only" and callers should keep using
    /// <c>CalloutBackgroundBrush</c> for the Border's Background.</summary>
    public static ResourceDictionary BuildResourceDictionary(LoadedTheme theme, IThemeFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(fontResolver);

        var manifest = theme.Manifest;
        var dictionary = new ResourceDictionary
        {
            ["CalloutBackgroundBrush"] = BrushFromHex(manifest.Brushes.CalloutBackgroundBrush),
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
            ["CalloutBackgroundImage"] = theme.Status == ThemeLoadStatus.Ok ? theme.BackgroundImage : null,
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
            // Manifests reaching this point are already strictly validated at parse time
            // (§4.1); this is defense-in-depth only, never expected to trigger.
        }

        return Brushes.Gray;
    }
}
