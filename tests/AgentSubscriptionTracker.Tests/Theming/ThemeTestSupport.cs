// SPEC-0004 — shared test fixtures and sample data for the theming test suite.
// Spec-phase stub: references AgentSubscriptionTracker.App.Theming types that do not
// exist yet, so the test project intentionally does not compile until TASK-016
// implements SPEC-0004.

using System.Threading;
using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.Tests.Theming;

public static class ThemeTestSupport
{
    /// <summary>Runs <paramref name="action"/> on a dedicated STA thread and re-throws any
    /// exception on the calling thread. WPF FrameworkElement/DependencyObject construction
    /// requires an STA thread (InputManager/KeyboardNavigation), but xUnit's default test
    /// thread is MTA; this is the standard non-invasive workaround used by WPF unit tests
    /// that need a real FrameworkElement without spinning up a full Application/Dispatcher.</summary>
    public static void RunOnSta(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw captured;
        }
    }

    public static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Theming", fileName);

    public static string ReadFixtureText(string fileName) => File.ReadAllText(FixturePath(fileName));

    public static byte[] ReadFixtureBytes(string fileName) => File.ReadAllBytes(FixturePath(fileName));

    /// <summary>A minimal well-formed manifest, equivalent in shape to the shipped
    /// "Dark" built-in, for tests that don't need to load it from a fixture file.</summary>
    public static ThemeManifest ValidDarkManifest() => new()
    {
        SchemaVersion = ThemeManifest.CurrentSchemaVersion,
        Name = "Dark",
        Background = new ThemeBackground
        {
            ImagePath = "background.png",
            FallbackColor = "#FF1F2430",
        },
        Fonts = new ThemeFontSet
        {
            Header = new ThemeFont { Family = "Segoe UI", Size = 13.0, Weight = "SemiBold" },
            Body = new ThemeFont { Family = "Segoe UI", Size = 12.0, Weight = "Normal" },
            Footer = new ThemeFont { Family = "Segoe UI", Size = 10.5, Weight = "Normal" },
        },
        Brushes = new ThemeBrushSet
        {
            CalloutBackgroundBrush = "#FF1F2430",
            CalloutBorderBrush = "#FF3A3F4B",
            TextPrimaryBrush = "#FFF5F5F5",
            TextSecondaryBrush = "#FFB7BCC7",
            BarTrackBrush = "#FF2C313D",
            SeparatorBrush = "#FF3A3F4B",
            AccentBrush = "#FF4C8DFF",
        },
        SeverityBands = new ThemeSeverityBands
        {
            Ok = "#FF35C46B",
            Warn = "#FFE0A22B",
            Critical = "#FFE05C5C",
        },
    };

    /// <summary>Same shape as <see cref="ValidDarkManifest"/> but with no background
    /// image (fallback color only) — used by tests that only exercise font/brush/
    /// severity logic.</summary>
    public static ThemeManifest ValidLightManifestNoImage() => new()
    {
        SchemaVersion = ThemeManifest.CurrentSchemaVersion,
        Name = "Light",
        Background = new ThemeBackground
        {
            ImagePath = null,
            FallbackColor = "#FFF7F8FA",
        },
        Fonts = new ThemeFontSet
        {
            Header = new ThemeFont { Family = "Segoe UI", Size = 13.0, Weight = "SemiBold" },
            Body = new ThemeFont { Family = "Segoe UI", Size = 12.0, Weight = "Normal" },
            Footer = new ThemeFont { Family = "Segoe UI", Size = 10.5, Weight = "Normal" },
        },
        Brushes = new ThemeBrushSet
        {
            CalloutBackgroundBrush = "#FFF7F8FA",
            CalloutBorderBrush = "#FFD6D9E0",
            TextPrimaryBrush = "#FF1B1D22",
            TextSecondaryBrush = "#FF5B5F69",
            BarTrackBrush = "#FFE6E8ED",
            SeparatorBrush = "#FFD6D9E0",
            AccentBrush = "#FF2F6FE0",
        },
        SeverityBands = new ThemeSeverityBands
        {
            Ok = "#FF1F9D52",
            Warn = "#FFC27C0E",
            Critical = "#FFC23A3A",
        },
    };
}
