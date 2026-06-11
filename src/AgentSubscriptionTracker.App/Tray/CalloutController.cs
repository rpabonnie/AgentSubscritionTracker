// SPEC-0003 §5.3 — callout open/close and DPI-aware positioning next to the tray icon.
// Not unit-tested (no UI automation); exercised by the TASK-011 human checkpoint.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSubscriptionTracker.App.ViewModels;
using AgentSubscriptionTracker.App.Views;

namespace AgentSubscriptionTracker.App.Tray;

/// <summary>
/// Shows the callout on hover (cached data instantly, background hover refresh), hides it
/// when the pointer has left both the icon and the callout for a ~300 ms grace period, and
/// positions it adjacent to the icon clamped to the monitor's working area.
/// </summary>
public sealed partial class CalloutController : IDisposable
{
    private const int GraceTicks = 3; // 3 × 100 ms pointer-watch ticks ≈ 300 ms grace.

    private readonly TrayIconHost _trayIcon;
    private readonly CalloutWindow _window;
    private readonly TrayViewModel _viewModel;
    private readonly DispatcherTimer _pointerWatch;
    private int _outsideTicks;
    private bool _disposed;

    public CalloutController(TrayIconHost trayIcon, CalloutWindow window, TrayViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(trayIcon);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewModel);

        _trayIcon = trayIcon;
        _window = window;
        _viewModel = viewModel;

        _pointerWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _pointerWatch.Tick += (_, _) => OnPointerWatchTick();

        _trayIcon.CalloutRequested += (_, _) => Show();
        _trayIcon.CalloutDismissRequested += (_, _) => HideUnlessPointerInside();
        _trayIcon.IconClicked += (_, _) => Toggle();
        _window.SizeChanged += (_, _) =>
        {
            if (_window.IsVisible)
            {
                PositionWindow();
            }
        };
    }

    /// <summary>Opens the callout (cached data shown instantly) and starts a hover refresh.</summary>
    public void Show()
    {
        if (_disposed)
        {
            return;
        }

        PositionWindow();
        _window.Show();
        PositionWindow(); // re-measure after layout so SizeToContent height is final
        _outsideTicks = 0;
        _pointerWatch.Start();

        // Fire-and-forget by contract: without a caller token, RequestRefreshAsync never throws.
        _ = _viewModel.RequestRefreshAsync(RefreshTrigger.Hover);
    }

    /// <summary>Hides the callout.</summary>
    public void Hide()
    {
        _pointerWatch.Stop();
        _window.Hide();
    }

    /// <summary>Left-click toggle.</summary>
    public void Toggle()
    {
        if (_window.IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pointerWatch.Stop();
        _window.Close();
    }

    private void HideUnlessPointerInside()
    {
        if (!_window.IsVisible || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        if (!IsInsideWindowDevice(cursor.X, cursor.Y))
        {
            Hide();
        }
    }

    private void OnPointerWatchTick()
    {
        if (!_window.IsVisible)
        {
            _pointerWatch.Stop();
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var insideIcon = _trayIcon.TryGetIconRect(out var iconRect)
            && iconRect.Contains(cursor.X, cursor.Y);
        if (insideIcon || IsInsideWindowDevice(cursor.X, cursor.Y))
        {
            _outsideTicks = 0;
            return;
        }

        if (++_outsideTicks >= GraceTicks)
        {
            Hide();
        }
    }

    private bool IsInsideWindowDevice(int x, int y)
    {
        if (PresentationSource.FromVisual(_window) is not { CompositionTarget: { } target })
        {
            return false;
        }

        var toDevice = target.TransformToDevice;
        var topLeft = toDevice.Transform(new Point(_window.Left, _window.Top));
        var bottomRight = toDevice.Transform(
            new Point(_window.Left + _window.ActualWidth, _window.Top + _window.ActualHeight));

        return x >= topLeft.X && x < bottomRight.X && y >= topLeft.Y && y < bottomRight.Y;
    }

    private void PositionWindow()
    {
        new WindowInteropHelper(_window).EnsureHandle();
        var fromDevice = PresentationSource.FromVisual(_window) is { CompositionTarget: { } target }
            ? target.TransformFromDevice
            : Matrix.Identity;

        var width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        var height = _window.ActualHeight > 0 ? _window.ActualHeight : 320;

        if (!_trayIcon.TryGetIconRect(out var icon))
        {
            // Fallback: bottom-right of the primary working area.
            var area = SystemParameters.WorkArea;
            _window.Left = area.Right - width;
            _window.Top = area.Bottom - height;
            return;
        }

        // Monitor working area for the monitor hosting the icon, converted to DIPs.
        var centerDevice = new NativeMethods.NativePoint
        {
            X = (icon.Left + icon.Right) / 2,
            Y = (icon.Top + icon.Bottom) / 2,
        };
        var monitor = NativeMethods.MonitorFromPoint(centerDevice, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new NativeMethods.MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            var area = SystemParameters.WorkArea;
            _window.Left = area.Right - width;
            _window.Top = area.Bottom - height;
            return;
        }

        var workTopLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
        var workBottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
        var iconTopLeft = fromDevice.Transform(new Point(icon.Left, icon.Top));
        var iconBottomRight = fromDevice.Transform(new Point(icon.Right, icon.Bottom));
        var iconCenter = new Point(
            (iconTopLeft.X + iconBottomRight.X) / 2, (iconTopLeft.Y + iconBottomRight.Y) / 2);
        const double margin = 4;

        // The taskbar lies outside the working area; the icon's position relative to the
        // working area tells us which edge the taskbar is docked to.
        double left, top;
        if (iconCenter.Y >= workBottomRight.Y)
        {
            left = iconCenter.X - (width / 2);
            top = iconTopLeft.Y - height - margin;
        }
        else if (iconCenter.Y <= workTopLeft.Y)
        {
            left = iconCenter.X - (width / 2);
            top = iconBottomRight.Y + margin;
        }
        else if (iconCenter.X >= workBottomRight.X)
        {
            left = iconTopLeft.X - width - margin;
            top = iconCenter.Y - (height / 2);
        }
        else
        {
            left = iconBottomRight.X + margin;
            top = iconCenter.Y - (height / 2);
        }

        _window.Left = Math.Clamp(left, workTopLeft.X, Math.Max(workTopLeft.X, workBottomRight.X - width));
        _window.Top = Math.Clamp(top, workTopLeft.Y, Math.Max(workTopLeft.Y, workBottomRight.Y - height));
    }

    /// <summary>P/Invoke surface (CA1060: isolated in a NativeMethods class).</summary>
    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out NativePoint point);

        [LibraryImport("user32.dll", EntryPoint = "MonitorFromPoint")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial nint MonitorFromPoint(NativePoint point, uint flags);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

        /// <summary>Mirror of the native POINT.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint
        {
            internal int X;
            internal int Y;
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

        /// <summary>Mirror of the native MONITORINFO.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct MonitorInfo
        {
            internal uint Size;
            internal NativeRect Monitor;
            internal NativeRect Work;
            internal uint Flags;
        }
    }
}
