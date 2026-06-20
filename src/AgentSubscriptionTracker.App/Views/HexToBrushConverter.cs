// SPEC-0004 §5.2 — converts a "#AARRGGBB"/"#RRGGBB" hex string (as typed into a theme-editor
// color field) into a Brush for the color-swatch preview. Never throws: an in-progress/invalid
// value while typing falls back to a neutral gray rather than erroring out of a binding.

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AgentSubscriptionTracker.App.Views;

/// <summary>One-way hex-string → <see cref="Brush"/> converter for the editor's color swatches.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
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
                // In-progress/invalid hex while typing — fall through to the neutral default.
            }
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
