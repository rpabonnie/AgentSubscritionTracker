// SPEC-0003 §5.2 — callout window code-behind: data-age ticker (1 s, only while visible)
// and the value converters used by the XAML templates. All display logic lives in the
// view-models; this file only re-renders the age line and maps severity to theme brushes.

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

        _ageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ageTimer.Tick += (_, _) => UpdateAgeText();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Footer "Refresh" button — the shell triggers a manual refresh.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Footer "Exit" button — the shell shuts the app down.</summary>
    public event EventHandler? ExitRequested;

    private void OnRefreshClick(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClick(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

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
        AgeText.Text = stale ? _viewModel.DataAgeText + " (cached)" : _viewModel.DataAgeText;
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

/// <summary>Maps <see cref="UsageSeverity"/> to the matching theme brush.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            UsageSeverity.Critical => "CriticalBrush",
            UsageSeverity.Warning => "WarningBrush",
            _ => "AccentBrush",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
