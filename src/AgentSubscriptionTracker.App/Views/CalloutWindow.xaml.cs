// SPEC-0003 §5.2 / SPEC-0004 §5.2 — callout window code-behind: data-age ticker (1 s, only
// while visible) and the value converters used by the XAML templates. All display logic
// lives in the view-models; this file only re-renders the age line, maps severity to theme
// brushes, and re-broadcasts the shared CalloutContent's footer button events.

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSubscriptionTracker.App.ViewModels;

namespace AgentSubscriptionTracker.App.Views;

/// <summary>The tray tooltip callout. DataContext is the <see cref="TrayViewModel"/>.</summary>
public sealed partial class CalloutWindow : Window
{
    private readonly TrayViewModel _viewModel;
    private readonly DispatcherTimer _ageTimer;

    public CalloutWindow(TrayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        CalloutContentControl.RefreshRequested += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        CalloutContentControl.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        CalloutContentControl.ThemeRequested += (_, _) => ThemeRequested?.Invoke(this, EventArgs.Empty);

        _ageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ageTimer.Tick += (_, _) => UpdateAgeText();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Footer "Refresh" button — the shell triggers a manual refresh.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Footer "Exit" button — the shell shuts the app down.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Footer "Theme" button — the shell opens the theme picker popup.</summary>
    public event EventHandler? ThemeRequested;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrayViewModel.DataAgeText)
            or nameof(TrayViewModel.Claude)
            or nameof(TrayViewModel.Copilot))
        {
            // The view-model may publish from a background continuation; marshal to the UI thread.
            Dispatcher.BeginInvoke(UpdateAgeText);
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            UpdateAgeText();
            _ageTimer.Start();
        }
        else
        {
            _ageTimer.Stop();
        }
    }

    private void UpdateAgeText()
    {
        var stale = _viewModel.Claude.IsStale || _viewModel.Copilot.IsStale;
        CalloutContentControl.AgeTextValue = stale ? _viewModel.DataAgeText + " (cached)" : _viewModel.DataAgeText;
    }
}

/// <summary>Collapses an element when the bound value is null.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps <see cref="UsageSeverity"/> to the active theme's severity brush.
/// SPEC-0004 §5.3 — resolves "ok"/"warn"/"critical"/"BarTrackBrush" from the bound
/// element's resource scope (DynamicResource-equivalent lookup) instead of static
/// converter-owned colors, so the same converter instance automatically reflects
/// whichever theme's resources are currently merged into that scope (including a theme
/// editor's preview-scoped dictionary). Resolution is never cached — repeated theme
/// swaps are reflected on the very next Convert/ResolveBarTrackBrush call.
/// Implements <see cref="IValueConverter"/> for the unit-tested single-value contract
/// (value=severity, parameter=resource-scope element) and <see cref="IMultiValueConverter"/>
/// so CalloutWindow.xaml can additionally supply the bound element as a Self-reference
/// binding (ConverterParameter is not itself bindable in WPF).</summary>
public sealed class SeverityToBrushConverter : IValueConverter, IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ResolveResource(parameter, KeyFor(value));

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        var severity = values.Length > 0 ? values[0] : null;
        var scope = values.Length > 1 ? values[1] : null;
        return ResolveResource(scope, KeyFor(severity));
    }

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>Resolves the active theme's BarTrackBrush from <paramref name="scope"/>'s
    /// resource scope, used by CalloutWindow.xaml's progress-bar track Background binding
    /// so track color resolution goes through the same theme-aware lookup as severity.
    /// Instance method (not static) — it is exposed as part of this converter's public
    /// per-instance contract (SPEC-0004 §5.3) even though it has no instance state.</summary>
#pragma warning disable CA1822
    public Brush ResolveBarTrackBrush(DependencyObject scope) => ResolveResource(scope, "BarTrackBrush");
#pragma warning restore CA1822

    private static string KeyFor(object? severityValue) => severityValue switch
    {
        UsageSeverity.Critical => "critical",
        UsageSeverity.Warning => "warn",
        _ => "ok",
    };

    private static Brush ResolveResource(object? scope, string key)
    {
        if (scope is FrameworkElement element && element.TryFindResource(key) is Brush elementBrush)
        {
            return elementBrush;
        }

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}

/// <summary>Resolves <c>BarTrackBrush</c> from the bound element's resource scope via a
/// single-value MultiBinding (the element itself, by Self-reference). SPEC-0004 §5.3.</summary>
public sealed class BarTrackBrushMultiConverter : IMultiValueConverter
{
    private readonly SeverityToBrushConverter _inner = new();

    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        var scope = values.Length > 0 ? values[0] : null;
        return scope is DependencyObject dependencyObject
            ? _inner.ResolveBarTrackBrush(dependencyObject)
            : Brushes.Gray;
    }

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
