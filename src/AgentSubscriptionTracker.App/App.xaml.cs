// SPEC-0003 §5.5 — composition root: single instance (named mutex; second launch signals
// the first to open the callout and exits 0), tray icon + callout wiring, themed context
// menu, and disposal of every native/HTTP resource on all exit paths.

using System.IO;
using System.Net.Http;
using System.Windows;
using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.App.Tray;
using AgentSubscriptionTracker.App.ViewModels;
using AgentSubscriptionTracker.App.Views;

namespace AgentSubscriptionTracker.App;

/// <summary>Tray-only application: no main window, explicit shutdown via the callout.</summary>
internal sealed partial class App : Application, IDisposable
{
    private const string SingleInstanceMutexName = @"Local\AgentSubscriptionTracker.SingleInstance";
    private const string ShowCalloutEventName = @"Local\AgentSubscriptionTracker.ShowCallout";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstance;
    private EventWaitHandle? _showCalloutSignal;
    private RegisteredWaitHandle? _showCalloutWait;
    private SocketsHttpHandler? _claudeHandler;
    private SocketsHttpHandler? _copilotHandler;
    private ClaudeUsageService? _claudeService;
    private CopilotQuotaService? _copilotService;
    private TrayIconHost? _trayIcon;
    private CalloutController? _callout;
    private TrayViewModel? _viewModel;
    private bool _disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstance = createdNew;
        if (!createdNew)
        {
            // Another instance runs: ask it to open its callout, then exit cleanly.
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowCalloutEventName))
            {
                signal.Set();
            }

            Shutdown(0);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ThemeDetector.ApplyTheme(this);

        var timeProvider = TimeProvider.System;
        _claudeHandler = new SocketsHttpHandler(); // TLS defaults untouched (CLAUDE.md)
        _copilotHandler = new SocketsHttpHandler();
        _claudeService = new ClaudeUsageService(new ClaudeCredentialsFileReader(), _claudeHandler, timeProvider);
        _copilotService = new CopilotQuotaService(
            new CopilotTokenProvider(new WindowsCredentialStore()), _copilotHandler, timeProvider);
        _viewModel = new TrayViewModel(_claudeService, _copilotService, timeProvider);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _trayIcon = new TrayIconHost(iconPath);
        _trayIcon.ThemeSettingChanged += (_, _) => ThemeDetector.ApplyTheme(this);

        // The callout's command row is the way to operate the app.
        var calloutWindow = new CalloutWindow(_viewModel);
        calloutWindow.RefreshRequested += (_, _) => _ = _viewModel.RequestRefreshAsync(RefreshTrigger.Manual);
        calloutWindow.ExitRequested += (_, _) => ExitApplication();
        _callout = new CalloutController(_trayIcon, calloutWindow, _viewModel);

        // Secondary launches signal this event; open the callout dispatcher-marshalled.
        _showCalloutSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowCalloutEventName);
        _showCalloutWait = ThreadPool.RegisterWaitForSingleObject(
            _showCalloutSignal,
            (_, _) => Dispatcher.BeginInvoke(() => _callout?.Show()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        // Crash-exit safety net: the tray icon must never outlive the process.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _trayIcon?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _showCalloutWait?.Unregister(null);
        _showCalloutSignal?.Dispose();
        _callout?.Dispose();
        _trayIcon?.Dispose();
        _claudeService?.Dispose();
        _copilotService?.Dispose();
        _claudeHandler?.Dispose();
        _copilotHandler?.Dispose();
        if (_ownsSingleInstance)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
    }

    private void ExitApplication()
    {
        _callout?.Dispose();
        _trayIcon?.Dispose();
        Shutdown();
    }
}
