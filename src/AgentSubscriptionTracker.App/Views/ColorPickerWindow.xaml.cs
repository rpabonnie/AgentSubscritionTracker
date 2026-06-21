// SPEC-0004 §5.2 — code-behind for the RGBA color picker. Drives a live preview swatch + hex
// readout from four 0–255 channel sliders, and paints each channel's track gradient so the
// picker reads like a modern color tool. Seeded from the field's current color; the chosen
// value is read back via SelectedColor when the dialog returns true.

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSubscriptionTracker.App.Utils;

namespace AgentSubscriptionTracker.App.Views;

/// <summary>Small modal RGBA color picker (SPEC-0004 §5.2). Not unit-tested — shell behavior
/// verified manually.</summary>
public sealed partial class ColorPickerWindow : Window
{
    /// <summary>Creates the picker seeded from <paramref name="initial"/>. <paramref name="chromeSource"/>
    /// is the pack URI of an Editor* chrome dictionary (light/dark) so the picker mirrors the
    /// editor's current appearance; null falls back to the dark chrome.</summary>
    public ColorPickerWindow(Color initial, string? chromeSource = null)
    {
        InitializeComponent();

        // Insert the chrome AFTER InitializeComponent so it sits at index 0, ahead of the
        // design system merged by Window.Resources; its Editor* brushes drive the chrome look.
        var source = chromeSource ?? "/Themes/EditorChromeDark.xaml";
        Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });

        SelectedColor = initial;

        var dark = source.Contains("Dark", StringComparison.OrdinalIgnoreCase);
        SourceInitialized += (_, _) => WindowTitleBarTheme.Apply(this, dark);

        RSlider.Value = initial.R;
        GSlider.Value = initial.G;
        BSlider.Value = initial.B;
        ASlider.Value = initial.A;
        UpdatePreview();
    }

    /// <summary>The currently-selected color; final value when <see cref="Window.ShowDialog"/>
    /// returns true.</summary>
    public Color SelectedColor { get; private set; }

    private void OnChannelChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();

    private void UpdatePreview()
    {
        // ValueChanged can fire while the named elements are still being initialized.
        if (PreviewSwatch is null || ASlider is null)
        {
            return;
        }

        var a = (byte)ASlider.Value;
        var r = (byte)RSlider.Value;
        var g = (byte)GSlider.Value;
        var b = (byte)BSlider.Value;

        SelectedColor = Color.FromArgb(a, r, g, b);
        PreviewSwatch.Fill = new SolidColorBrush(SelectedColor);
        HexText.Text = string.Create(CultureInfo.InvariantCulture, $"#{a:X2}{r:X2}{g:X2}{b:X2}");
        RValue.Text = r.ToString(CultureInfo.InvariantCulture);
        GValue.Text = g.ToString(CultureInfo.InvariantCulture);
        BValue.Text = b.ToString(CultureInfo.InvariantCulture);
        AValue.Text = a.ToString(CultureInfo.InvariantCulture);

        // Paint each channel's rail to show that channel sweeping min→max over the current color
        // (RGB at full opacity for clarity; alpha transparent→opaque over the checkerboard).
        RSlider.Background = Rail(Color.FromRgb(0, g, b), Color.FromRgb(255, g, b));
        GSlider.Background = Rail(Color.FromRgb(r, 0, b), Color.FromRgb(r, 255, b));
        BSlider.Background = Rail(Color.FromRgb(r, g, 0), Color.FromRgb(r, g, 255));
        ASlider.Background = Rail(Color.FromArgb(0, r, g, b), Color.FromArgb(255, r, g, b));
    }

    private static LinearGradientBrush Rail(Color from, Color to)
    {
        var brush = new LinearGradientBrush(from, to, new Point(0, 0.5), new Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
