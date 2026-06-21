// Theme-UI design system (theme editor / picker / color picker chrome) — small attached
// properties that let one shared ControlTemplate carry per-variant hover/pressed fills.
// WPF Button has no "HoverBackground"; rather than duplicate the whole template per variant
// (accent vs subtle vs danger), each style sets these attached brushes and the shared
// template binds its hover/pressed triggers to them via the templated parent.

using System.Windows;
using System.Windows.Media;

namespace AgentSubscriptionTracker.App.Views;

/// <summary>Attached state brushes consumed by the shared button/segment control templates
/// in <c>Themes/EditorDesignSystem.xaml</c> so one template serves every button variant.</summary>
public static class UiState
{
    /// <summary>Background painted while the pointer is over the control.</summary>
    public static readonly DependencyProperty HoverBrushProperty =
        DependencyProperty.RegisterAttached(
            "HoverBrush", typeof(Brush), typeof(UiState), new PropertyMetadata(null));

    public static Brush? GetHoverBrush(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(HoverBrushProperty);
    }

    public static void SetHoverBrush(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HoverBrushProperty, value);
    }

    /// <summary>Background painted while the control is pressed.</summary>
    public static readonly DependencyProperty PressedBrushProperty =
        DependencyProperty.RegisterAttached(
            "PressedBrush", typeof(Brush), typeof(UiState), new PropertyMetadata(null));

    public static Brush? GetPressedBrush(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(PressedBrushProperty);
    }

    public static void SetPressedBrush(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PressedBrushProperty, value);
    }
}
