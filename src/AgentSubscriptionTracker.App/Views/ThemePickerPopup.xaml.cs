// SPEC-0004 §5.1 — non-activating theme picker popup code-behind. Pure shell wiring: row
// click selects that theme, Edit opens the editor seeded from that theme, "Add new theme"
// opens the editor seeded from a clone of a designated built-in.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AgentSubscriptionTracker.App.Theming;
using AgentSubscriptionTracker.App.ViewModels;

namespace AgentSubscriptionTracker.App.Views;

/// <summary>Carries the theme id chosen in <see cref="ThemePickerPopup"/>.</summary>
public sealed class ThemeSelectedEventArgs(string themeId) : EventArgs
{
    public string ThemeId { get; } = themeId;
}

/// <summary>Carries the theme entry chosen for editing in <see cref="ThemePickerPopup"/>.</summary>
public sealed class ThemeEditRequestedEventArgs(ThemeListEntryViewModel entry) : EventArgs
{
    public ThemeListEntryViewModel Entry { get; } = entry;
}

/// <summary>Raised after a theme row was successfully deleted, so the shell can refresh
/// the picker's backing repository state.</summary>
public sealed class ThemeDeletedEventArgs(string themeId) : EventArgs
{
    public string ThemeId { get; } = themeId;
}

/// <summary>The theme picker popup (SPEC-0004 §5.1). Not unit-tested — shell behavior
/// verified manually per SPEC-0004 §5/§6.</summary>
public sealed partial class ThemePickerPopup : Window
{
    private readonly ThemePickerViewModel _viewModel;
    private readonly IThemeFontResolver _fontResolver;
    private readonly SamplePreviewViewModel _sampleData;

    public ThemePickerPopup(ThemePickerViewModel viewModel, IThemeFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(fontResolver);
        _viewModel = viewModel;
        _fontResolver = fontResolver;

        // One shared static sample so every row's live preview shows the same realistic
        // callout content, differing only by the theme applied to it.
        _sampleData = SamplePreviewData.Build();

        InitializeComponent();
        ThemeListItems.ItemsSource = viewModel.Entries;

        // The popup opens non-activating (ShowActivated=False) so it doesn't steal focus the
        // instant it appears — but to dismiss on Esc / click-away it needs keyboard focus. Grab
        // it AFTER the shell finishes positioning (Background priority runs once OnThemeButton-
        // Clicked's synchronous Show + PositionNextTo calls have completed), so activation can't
        // race the placement that reads the still-visible callout's bounds.
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Activate();
            Focus();
        });
    }

    private bool _closing;

    /// <summary>Marks the window as closing the instant ANY close path begins (this window's own
    /// dismiss handlers, or the shell calling <see cref="Window.Close"/> to open the editor). Once
    /// set, <see cref="CloseSafely"/> and <see cref="OnDeactivated"/> never call
    /// <see cref="Window.Close"/> again — WPF throws "Cannot call Close while a Window is closing"
    /// if a deactivation message arrives mid-close (the activated picker loses focus as it closes
    /// to open the editor).</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    /// <summary>Closes once; guards against re-entrancy from overlapping dismiss paths
    /// (Esc, deactivation, the close button) and from a close already in progress.</summary>
    private void CloseSafely()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }

    /// <summary>Esc dismisses the picker without changing the active theme.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e is { Key: Key.Escape, Handled: false })
        {
            CloseSafely();
            e.Handled = true;
        }
    }

    /// <summary>Click-away (losing activation) dismisses the picker, like a flyout — unless a
    /// close is already underway (see <see cref="OnClosing"/>).</summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!_closing)
        {
            CloseSafely();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseSafely();

    /// <summary>SPEC-0004 §5.1 — themes one row's live preview. Each <see cref="CalloutContent"/>
    /// is bound to the shared sample data and gets its OWN preview-scoped resource dictionary
    /// (via <see cref="ThemeResourceApplier.ApplyDirect"/>) built from that row's theme, so
    /// every row renders the real callout in its own colors/fonts without touching
    /// <c>Application.Current.Resources</c>.</summary>
    private void OnPreviewLoaded(object sender, RoutedEventArgs e)
    {
        // At Loaded the inherited DataContext is still the row's entry; capture it before we
        // override DataContext with the sample data the CalloutContent bindings expect.
        if (sender is not CalloutContent content || content.DataContext is not ThemeListEntryViewModel entry)
        {
            return;
        }

        content.DataContext = _sampleData;
        ThemeResourceApplier.ApplyDirect(content.Resources, entry.Theme, _fontResolver);
    }

    /// <summary>SPEC-0004 §5.1 — places this popup next to the live callout instead of the
    /// monitor's top-left. Prefers the LEFT side of <paramref name="anchor"/>, top-aligned,
    /// with an 8px gap; flips to the RIGHT side if the left placement falls off the work
    /// area's left edge; falls back to the bottom-right of the work area when the anchor is
    /// not a usable, visible window. Final placement is clamped into <paramref name="workArea"/>
    /// — pass the callout's own monitor work area (see
    /// <c>CalloutController.GetCalloutMonitorWorkArea</c>) so the popup stays on the SAME
    /// monitor the callout is on, not necessarily the primary one. Call once before
    /// <see cref="Window.Show"/> and once after, so the second call clamps Top against the
    /// finalized SizeToContent <see cref="FrameworkElement.ActualHeight"/>.</summary>
    public void PositionNextTo(Window? anchor, Rect workArea)
    {
        const double gap = 8;
        var area = workArea;

        double left;
        double top;
        if (anchor is null || !anchor.IsVisible || anchor.ActualWidth <= 0)
        {
            // No usable callout: dock to the bottom-right corner of the work area.
            left = area.Right - Width - gap;
            top = area.Bottom - ActualHeight - gap;
        }
        else
        {
            // Prefer the left side of the callout, top-aligned.
            left = anchor.Left - Width - gap;
            if (left < area.Left)
            {
                // Off the left edge — flip to the right side.
                left = anchor.Left + anchor.ActualWidth + gap;
            }

            top = anchor.Top;
        }

        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }

    /// <summary>Raised when the user picks a theme row to activate.</summary>
    public event EventHandler<ThemeSelectedEventArgs>? ThemeSelected;

    /// <summary>Raised when the user clicks a row's Edit button.</summary>
    public event EventHandler<ThemeEditRequestedEventArgs>? EditThemeRequested;

    /// <summary>Raised after a row's Delete button successfully deletes that theme
    /// (§4.4 edge case #11). Not raised when the deletion is refused.</summary>
    public event EventHandler<ThemeDeletedEventArgs>? ThemeDeleted;

    /// <summary>Raised when the user clicks "Add new theme".</summary>
    public event EventHandler? AddNewThemeRequested;

    private void OnSelectClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ThemeListEntryViewModel entry })
        {
            ThemeSelected?.Invoke(this, new ThemeSelectedEventArgs(entry.ThemeId));
            Close();
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ThemeListEntryViewModel entry })
        {
            // The shell (App) closes this popup and reopens a fresh one around the editor.
            EditThemeRequested?.Invoke(this, new ThemeEditRequestedEventArgs(entry));
        }
    }

    private void OnAddNewThemeClick(object sender, RoutedEventArgs e) =>
        AddNewThemeRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Refuses (showing a message, popup stays open) for the active theme or a
    /// built-in (§4.4 edge case #11); otherwise deletes and closes so the shell can
    /// refresh the picker from a fresh load.</summary>
    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ThemeListEntryViewModel entry })
        {
            return;
        }

        var refusalMessage = _viewModel.TryDelete(entry);
        if (refusalMessage is not null)
        {
            DeleteRefusalMessage.Text = refusalMessage;
            DeleteRefusalMessage.Visibility = Visibility.Visible;
            return;
        }

        ThemeDeleted?.Invoke(this, new ThemeDeletedEventArgs(entry.ThemeId));
        Close();
    }
}
