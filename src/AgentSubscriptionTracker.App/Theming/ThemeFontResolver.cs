// SPEC-0004 §4 — font family resolution against installed system fonts, with graceful
// fallback to Segoe UI. Never throws.

using System.IO;
using System.Windows;
using System.Windows.Media;

namespace AgentSubscriptionTracker.App.Theming;

/// <summary>A font family resolved against installed system fonts, with fallback.</summary>
public readonly record struct ResolvedThemeFont(
    FontFamily Family,
    double Size,
    FontWeight Weight,
    /// <summary>True when the manifest's requested family was not installed and
    /// "Segoe UI" was substituted.</summary>
    bool FellBackToDefault);

public interface IThemeFontResolver
{
    /// <summary>Looks up <paramref name="font"/>.Family in
    /// System.Windows.Media.Fonts.SystemFontFamilies (case-insensitive). Not found ⇒
    /// substitutes "Segoe UI" and sets FellBackToDefault; never throws. Unknown
    /// Weight string ⇒ FontWeights.Normal (does not affect FellBackToDefault).</summary>
    ResolvedThemeFont Resolve(ThemeFont font);
}

/// <summary>Resolves theme fonts against installed system fonts (SPEC-0004 §4).</summary>
public sealed class ThemeFontResolver : IThemeFontResolver
{
    private const string FallbackFamilyName = "Segoe UI";

    /// <inheritdoc />
    public ResolvedThemeFont Resolve(ThemeFont font)
    {
        ArgumentNullException.ThrowIfNull(font);

        var (family, fellBack) = ResolveFamily(font.Family);
        var weight = ResolveWeight(font.Weight);
        return new ResolvedThemeFont(family, font.Size, weight, fellBack);
    }

    private static (FontFamily Family, bool FellBack) ResolveFamily(string requestedFamily)
    {
        try
        {
            foreach (var installed in Fonts.SystemFontFamilies)
            {
                if (string.Equals(installed.Source, requestedFamily, StringComparison.OrdinalIgnoreCase))
                {
                    return (new FontFamily(requestedFamily), false);
                }
            }
        }
        catch (IOException)
        {
            // Never throw on font enumeration failures — fall through to the safe default.
        }

        return (new FontFamily(FallbackFamilyName), true);
    }

    private static FontWeight ResolveWeight(string weight) => weight.ToUpperInvariant() switch
    {
        "NORMAL" => FontWeights.Normal,
        "SEMIBOLD" => FontWeights.SemiBold,
        "BOLD" => FontWeights.Bold,
        "LIGHT" => FontWeights.Light,
        _ => FontWeights.Normal,
    };
}
