// SPEC-0004 §5.2 — code-behind for the shared callout content UserControl. Exposes the
// footer button click events the host (CalloutWindow or the theme editor's preview) wires
// up; the editor preview leaves Refresh/Exit/Theme unwired (or wires Theme to no-op) since
// it only ever shows static sample data, never live API calls.

using System.Windows;
using System.Windows.Controls;

namespace AgentSubscriptionTracker.App.Views;

/// <summary>The callout's visual content, shared between <see cref="CalloutWindow"/> and the
/// theme editor's live preview (SPEC-0004 §5.2).</summary>
public sealed partial class CalloutContent : UserControl
{
    public CalloutContent()
    {
        InitializeComponent();
    }

    /// <summary>Footer "Refresh" button.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Footer "Exit" button.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Footer "Theme" button — opens the theme picker popup.</summary>
    public event EventHandler? ThemeRequested;

    /// <summary>Updates the data-age footer text.</summary>
    public string AgeTextValue
    {
        get => AgeText.Text;
        set => AgeText.Text = value;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClick(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnThemeClick(object sender, RoutedEventArgs e) =>
        ThemeRequested?.Invoke(this, EventArgs.Empty);
}
