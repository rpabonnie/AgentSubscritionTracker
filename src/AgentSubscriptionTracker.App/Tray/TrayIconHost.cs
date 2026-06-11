// SPEC-0003 §5.1 — raw Shell_NotifyIcon (NOTIFYICON_VERSION_4) tray icon host on a hidden
// top-level window. No WinForms. Not unit-tested (no UI automation); exercised by the
// TASK-011 human checkpoint.

using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AgentSubscriptionTracker.App.Tray;

/// <summary>Screen rectangle of the tray icon, in device pixels.</summary>
public readonly record struct TrayIconRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>True when the device-pixel point lies inside this rectangle.</summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>
/// Owns the notification-area icon: NIM_ADD/NIM_SETVERSION on startup, re-add on Explorer
/// restart (TaskbarCreated broadcast), NIM_DELETE on every exit path. Raises hover/click
/// events for the shell; a mouse-dwell timer (~400 ms) covers shells that never deliver
/// NIN_POPUPOPEN.
/// </summary>
public sealed partial class TrayIconHost : IDisposable
{
    private const uint IconId = 1;
    private const int TrayCallbackMessage = 0x8001; // WM_APP + 1

    private const int WmMouseMove = 0x0200;
    private const int WmLButtonUp = 0x0202;
    private const int WmSettingChange = 0x001A;
    private const int NinSelect = 0x0400;
    private const int NinKeySelect = 0x0401;
    private const int NinPopupOpen = 0x0406;
    private const int NinPopupClose = 0x0407;

    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NifMessage = 0x01;
    private const uint NifIcon = 0x02;
    private const uint NotifyIconVersion4 = 4;

    private readonly HwndSource _messageWindow;
    private readonly int _taskbarCreatedMessage;
    private readonly DispatcherTimer _dwellTimer;
    private nint _iconHandle;
    private bool _disposed;

    public TrayIconHost(string iconPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);

        // A hidden top-level window (not message-only) so TaskbarCreated broadcasts arrive.
        var parameters = new HwndSourceParameters("AgentSubscriptionTracker")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);

        _taskbarCreatedMessage = unchecked((int)NativeMethods.RegisterWindowMessage("TaskbarCreated"));
        _iconHandle = LoadTrayIcon(iconPath);

        _dwellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _dwellTimer.Tick += (_, _) =>
        {
            _dwellTimer.Stop();
            CalloutRequested?.Invoke(this, EventArgs.Empty);
        };

        AddIcon();
    }

    /// <summary>The shell wants the rich tooltip callout opened (NIN_POPUPOPEN or mouse dwell).</summary>
    public event EventHandler? CalloutRequested;

    /// <summary>The shell wants the callout dismissed (NIN_POPUPCLOSE).</summary>
    public event EventHandler? CalloutDismissRequested;

    /// <summary>Left-click / keyboard select on the icon (toggle callout).</summary>
    public event EventHandler? IconClicked;

    /// <summary>WM_SETTINGCHANGE with "ImmersiveColorSet" — the app theme flipped.</summary>
    public event EventHandler? ThemeSettingChanged;

    /// <summary>Screen rectangle of the icon (device pixels) for callout placement.</summary>
    public bool TryGetIconRect(out TrayIconRect rect)
    {
        var identifier = new NativeMethods.NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconIdentifier>(),
            WindowHandle = _messageWindow.Handle,
            IconId = IconId,
        };

        if (NativeMethods.Shell_NotifyIconGetRect(in identifier, out var native) == 0)
        {
            rect = new(native.Left, native.Top, native.Right, native.Bottom);
            return true;
        }

        rect = default;
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DeleteIcon();
        if (_iconHandle != 0)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = 0;
        }

        // The ProcessExit safety net may dispose from a worker thread: the icon is already
        // deleted above (the part that must never be skipped); HwndSource and DispatcherTimer
        // are thread-affine, so only touch them on their own dispatcher thread.
        if (_messageWindow.Dispatcher.CheckAccess())
        {
            _dwellTimer.Stop();
            _messageWindow.RemoveHook(WndProc);
            _messageWindow.Dispose();
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_disposed)
        {
            return 0;
        }

        if (msg == _taskbarCreatedMessage)
        {
            AddIcon(); // Explorer restarted; the previous registration is gone.
            return 0;
        }

        if (msg == WmSettingChange)
        {
            if (lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
            {
                ThemeSettingChanged?.Invoke(this, EventArgs.Empty);
            }

            return 0;
        }

        if (msg != TrayCallbackMessage)
        {
            return 0;
        }

        // NOTIFYICON_VERSION_4: LOWORD(lParam) is the notification event.
        switch (unchecked((int)lParam) & 0xFFFF)
        {
            case NinPopupOpen:
                _dwellTimer.Stop();
                CalloutRequested?.Invoke(this, EventArgs.Empty);
                break;
            case NinPopupClose:
                _dwellTimer.Stop();
                CalloutDismissRequested?.Invoke(this, EventArgs.Empty);
                break;
            case WmMouseMove:
                // Dwell fallback for shells that never send popup notifications.
                _dwellTimer.Stop();
                _dwellTimer.Start();
                break;
            case WmLButtonUp:
            case NinSelect:
            case NinKeySelect:
                _dwellTimer.Stop();
                IconClicked?.Invoke(this, EventArgs.Empty);
                break;
            default:
                break;
        }

        handled = true;
        return 0;
    }

    private unsafe void AddIcon()
    {
        var data = NewIconData();
        data.Flags = NifMessage | NifIcon;
        data.CallbackMessage = TrayCallbackMessage;
        data.IconHandle = _iconHandle;

        NativeMethods.Shell_NotifyIcon(NimAdd, ref data);
        data.VersionOrTimeout = NotifyIconVersion4;
        NativeMethods.Shell_NotifyIcon(NimSetVersion, ref data);
    }

    private void DeleteIcon()
    {
        var data = NewIconData();
        NativeMethods.Shell_NotifyIcon(NimDelete, ref data);
    }

    private NativeMethods.NotifyIconData NewIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        WindowHandle = _messageWindow.Handle,
        IconId = IconId,
    };

    private static nint LoadTrayIcon(string iconPath)
    {
        const int SmCxSmIcon = 49;
        const int SmCySmIcon = 50;
        const uint ImageIcon = 1;
        const uint LrLoadFromFile = 0x10;

        var handle = NativeMethods.LoadImage(
            0,
            iconPath,
            ImageIcon,
            NativeMethods.GetSystemMetrics(SmCxSmIcon),
            NativeMethods.GetSystemMetrics(SmCySmIcon),
            LrLoadFromFile);

        return handle != 0
            ? handle
            : throw new FileNotFoundException("Tray icon asset not found or not loadable.", iconPath);
    }

    /// <summary>P/Invoke surface (CA1060: isolated in a NativeMethods class).</summary>
    private static partial class NativeMethods
    {
        [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

        [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int Shell_NotifyIconGetRect(
            in NotifyIconIdentifier identifier, out NativeRect iconLocation);

        [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW",
            StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial uint RegisterWindowMessage(string message);

        [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial nint LoadImage(
            nint instance, string name, uint type, int cx, int cy, uint flags);

        [LibraryImport("user32.dll", EntryPoint = "DestroyIcon")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyIcon(nint icon);

        [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int GetSystemMetrics(int index);

        /// <summary>Mirror of the native NOTIFYICONDATAW (version-4 layout).</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct NotifyIconData
        {
            internal uint Size;
            internal nint WindowHandle;
            internal uint IconId;
            internal uint Flags;
            internal int CallbackMessage;
            internal nint IconHandle;
            internal fixed char Tip[128];
            internal uint State;
            internal uint StateMask;
            internal fixed char Info[256];
            internal uint VersionOrTimeout;
            internal fixed char InfoTitle[64];
            internal uint InfoFlags;
            internal Guid ItemGuid;
            internal nint BalloonIconHandle;
        }

        /// <summary>Mirror of the native NOTIFYICONIDENTIFIER.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NotifyIconIdentifier
        {
            internal uint Size;
            internal nint WindowHandle;
            internal uint IconId;
            internal Guid ItemGuid;
        }

        /// <summary>Mirror of the native RECT.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }
    }
}
