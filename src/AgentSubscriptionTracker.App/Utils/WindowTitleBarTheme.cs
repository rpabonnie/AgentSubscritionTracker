// Opts a window's native title bar into Win11 dark/light mode so a dark editor window does not
// pair a dark body with a bright system title bar (a key "2000s dialog" tell). Pure BCL P/Invoke
// (dwmapi) mirroring the source-generated LibraryImport pattern already used in
// Services/WindowsCredentialStore.cs — no new dependency. No-op (swallows failures) on OS
// builds that don't support the attribute, so it never breaks window creation.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AgentSubscriptionTracker.App.Utils;

/// <summary>Applies the immersive dark-mode title-bar attribute to a WPF <see cref="Window"/>.</summary>
public static partial class WindowTitleBarTheme
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE (20) on Windows 10 20H1+ / Windows 11.
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Sets the window's title bar to dark or light. Safe to call before the window is
    /// shown (forces handle creation) and again whenever the editor's appearance toggles.</summary>
    public static void Apply(Window window, bool dark)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (handle == nint.Zero)
        {
            return;
        }

        int value = dark ? 1 : 0;
        // Best-effort: an unsupported OS returns a non-zero HRESULT, which we intentionally ignore.
        _ = NativeMethods.DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    private static partial class NativeMethods
    {
        [LibraryImport("dwmapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    }
}
