using System.Windows;

namespace AgentSubscriptionTracker.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
internal sealed partial class App : Application
{
    /// <summary>
    /// Creates and shows the main window. Explicit instantiation (instead of
    /// StartupUri) keeps the window's usage visible to static analysis.
    /// </summary>
    /// <param name="e">Startup event arguments.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MainWindow mainWindow = new();
        mainWindow.Show();
    }
}
