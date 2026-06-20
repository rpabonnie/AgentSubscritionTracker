// SPEC-0003 §5.5 / SPEC-0004 §5 — composition root: single instance (named mutex; second
// launch signals the first to open the callout and exits 0), tray icon + callout wiring,
// the JSON-manifest theme repository (load/re-seed/apply at startup, picker + editor
// wiring), and disposal of every native/HTTP resource on all exit paths.

using System.IO;
using System.Net.Http;
using System.Windows;
using AgentSubscriptionTracker.App.Services;
using AgentSubscriptionTracker.App.Theming;
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
    private CalloutWindow? _calloutWindow;
    private TrayViewModel? _viewModel;
    private ThemeLoader? _themeLoader;
    private IThemeStore? _themeStore;
    private IThemeFontResolver? _fontResolver;
    private string? _activeThemeId;
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

        var themesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentSubscriptionTracker",
            "themes");
        _themeLoader = new ThemeLoader(themesRoot);
        _themeStore = new ThemeStore(themesRoot);
        _fontResolver = new ThemeFontResolver();
        ApplyInitialTheme();

        var timeProvider = TimeProvider.System;
        _claudeHandler = new SocketsHttpHandler(); // TLS defaults untouched (CLAUDE.md)
        _copilotHandler = new SocketsHttpHandler();
        _claudeService = new ClaudeUsageService(new ClaudeCredentialsFileReader(), _claudeHandler, timeProvider);
        _copilotService = new CopilotQuotaService(
            new CopilotTokenProvider(new WindowsCredentialStore()), _copilotHandler, timeProvider);
        _viewModel = new TrayViewModel(_claudeService, _copilotService, timeProvider);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _trayIcon = new TrayIconHost(iconPath);
        _trayIcon.ThemeSettingChanged += (_, _) => ApplyOsThemeIfNoUserChoiceYet();

        // The callout's command row is the way to operate the app.
        _calloutWindow = new CalloutWindow(_viewModel);
        _calloutWindow.RefreshRequested += (_, _) => _ = _viewModel.RequestRefreshAsync(RefreshTrigger.Manual);
        _calloutWindow.ExitRequested += (_, _) => ExitApplication();
        _calloutWindow.ThemeRequested += (_, _) => OnThemeButtonClicked();
        _callout = new CalloutController(_trayIcon, _calloutWindow, _viewModel);

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

    /// <summary>Loads the theme repository and applies the active theme at startup. The OS
    /// dark/light heuristic (SPEC-0003 §5.4) seeds the *initial* selection only when no
    /// active theme is already known (first run); thereafter the user's theme-picker choice
    /// is what <see cref="IThemeLoader.LoadAll"/>'s hint preserves across restarts within
    /// this process's lifetime (SPEC-0004 §1 cross-spec contract).</summary>
    private void ApplyInitialTheme()
    {
        var osHint = ThemeDetector.IsDarkTheme() ? ThemeLoader.BuiltInDarkThemeId : ThemeLoader.BuiltInLightThemeId;
        var state = _themeLoader!.LoadAll(_activeThemeId ?? osHint);
        ApplyThemeState(state);
    }

    /// <summary>Re-applies the OS-appropriate built-in only when the user has never made an
    /// explicit theme-picker choice this session (mirrors SPEC-0003 §5.4's live
    /// WM_SETTINGCHANGE behavior without overriding an explicit user override).</summary>
    private void ApplyOsThemeIfNoUserChoiceYet()
    {
        if (_activeThemeId is not null)
        {
            return;
        }

        ApplyInitialTheme();
    }

    private void ApplyThemeState(ThemeRepositoryState state)
    {
        _activeThemeId = state.ActiveThemeId;
        var active = state.Themes.First(t => t.ThemeId == state.ActiveThemeId);
        ThemeResourceApplier.Apply(Resources, active, _fontResolver!);
    }

    private void OnThemeButtonClicked()
    {
        var state = _themeLoader!.LoadAll(_activeThemeId);
        var pickerViewModel = new ThemePickerViewModel(state, _themeStore);
        var popup = new ThemePickerPopup(pickerViewModel, _fontResolver!);
        popup.ThemeSelected += (_, args) => OnThemeSelected(args.ThemeId);
        popup.EditThemeRequested += (_, args) => EditFromPicker(popup, () => OpenEditor(args.Entry.Theme));
        popup.AddNewThemeRequested += (_, _) => EditFromPicker(popup, () => OnAddNewTheme(state));
        popup.ThemeDeleted += (_, _) => OnThemeButtonClicked();

        // Position beside the live callout, clamped to the callout's OWN monitor work area
        // (not the primary monitor) so the popup follows the tray to a secondary display.
        // Mirror CalloutController.Show()'s position/Show/reposition trick so the
        // SizeToContent height is final on the second placement.
        var workArea = _callout!.GetCalloutMonitorWorkArea();
        popup.PositionNextTo(_calloutWindow, workArea);
        popup.Show();
        popup.PositionNextTo(_calloutWindow, workArea);
    }

    /// <summary>Closes the picker so it isn't floating over the (modal) editor, runs the
    /// editor, then reopens a freshly-loaded picker once the editor closes so it reflects any
    /// new/edited theme. The editor's <see cref="ThemeEditorWindow.ShowDialog"/> blocks until
    /// it closes, so this reads top-to-bottom.</summary>
    private void EditFromPicker(ThemePickerPopup popup, Action openEditor)
    {
        popup.Close();
        openEditor();
        OnThemeButtonClicked();
    }

    private void OnThemeSelected(string themeId)
    {
        var state = _themeLoader!.LoadAll(themeId);
        ApplyThemeState(state);
    }

    private void OnAddNewTheme(ThemeRepositoryState state)
    {
        var seedTheme = state.Themes.FirstOrDefault(t => t.ThemeId == ThemeLoader.BuiltInDarkThemeId)
            ?? state.Themes.First(t => t.IsBuiltIn);
        OpenEditorAsNew(seedTheme.Manifest);
    }

    private void OpenEditor(LoadedTheme theme)
    {
        var viewModel = new ThemeEditorViewModel(
            theme.Manifest, _themeStore!, _fontResolver!, new ThemeBackgroundImageValidator(),
            theme.ThemeId, theme.IsBuiltIn, theme.FolderPath);
        ShowEditor(viewModel);
    }

    private void OpenEditorAsNew(ThemeManifest seedManifest)
    {
        var viewModel = new ThemeEditorViewModel(
            seedManifest, _themeStore!, _fontResolver!, new ThemeBackgroundImageValidator(),
            sourceThemeId: null, sourceIsBuiltIn: false, sourceFolderPath: null);
        ShowEditor(viewModel);
    }

    private void ShowEditor(ThemeEditorViewModel viewModel)
    {
        var savedThemeId = new SavedThemeIdHolder();
        viewModel.Saved += (_, args) => savedThemeId.ThemeId = args.ThemeId;

        var editor = new ThemeEditorWindow(viewModel, _fontResolver!, _callout);
        editor.ShowDialog();

        // Live-update only when the saved theme is currently active (§5.2); otherwise the
        // picker simply reflects the new/edited theme next time it is opened.
        if (savedThemeId.ThemeId is { } themeId && themeId == _activeThemeId)
        {
            OnThemeSelected(themeId);
        }
    }

    /// <summary>Mutable box so the analyzer can't (incorrectly) treat the captured value as
    /// provably null after <see cref="ThemeEditorWindow.ShowDialog"/> returns — the closure
    /// genuinely may have run synchronously during that call.</summary>
    private sealed class SavedThemeIdHolder
    {
        public string? ThemeId { get; set; }
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
