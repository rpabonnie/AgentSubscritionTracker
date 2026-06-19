// SPEC-0004 §5.1 — non-activating theme picker popup code-behind. Pure shell wiring: row
// click selects that theme, Edit opens the editor seeded from that theme, "Add new theme"
// opens the editor seeded from a clone of a designated built-in.

using System.Windows;
using System.Windows.Controls;
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

    public ThemePickerPopup(ThemePickerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        ThemeListItems.ItemsSource = viewModel.Entries;
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
            EditThemeRequested?.Invoke(this, new ThemeEditRequestedEventArgs(entry));
            Close();
        }
    }

    private void OnAddNewThemeClick(object sender, RoutedEventArgs e)
    {
        AddNewThemeRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

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
