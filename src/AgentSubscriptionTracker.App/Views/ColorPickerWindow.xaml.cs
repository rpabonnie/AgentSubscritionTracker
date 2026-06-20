// SPEC-0004 §5.2 — code-behind for the RGBA color picker. Drives a live preview swatch + hex
// readout from four 0–255 channel sliders. Seeded from the field's current color; the chosen
// value is read back via SelectedColor when the dialog returns true.

using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AgentSubscriptionTracker.App.Views;

/// <summary>Small modal RGBA color picker (SPEC-0004 §5.2). Not unit-tested — shell behavior
/// verified manually.</summary>
public sealed partial class ColorPickerWindow : Window
{
    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        SelectedColor = initial;

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
        if (PreviewSwatch is null)
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
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
