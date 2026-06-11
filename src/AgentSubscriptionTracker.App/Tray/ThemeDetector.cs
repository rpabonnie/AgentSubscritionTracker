// SPEC-0003 §5.4 — dark/light detection from HKCU AppsUseLightTheme (read-only, no secrets)
// and theme resource-dictionary swap. Live switching is driven by TrayIconHost's
// WM_SETTINGCHANGE("ImmersiveColorSet") event.

using System.Windows;
using Microsoft.Win32;

namespace AgentSubscriptionTracker.App.Tray;

/// <summary>Detects the Windows app theme and applies the matching resource dictionary.</summary>
public static class ThemeDetector
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when apps should use the dark theme (AppsUseLightTheme == 0; missing ⇒ light).</summary>
    public static bool IsDarkTheme() =>
        Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is 0;

    /// <summary>Swaps the application's merged theme dictionary to match the current Windows theme.</summary>
    public static void ApplyTheme(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var source = new Uri(
            IsDarkTheme() ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var theme = new ResourceDictionary { Source = source };

        application.Resources.MergedDictionaries.Clear();
        application.Resources.MergedDictionaries.Add(theme);
    }
}
