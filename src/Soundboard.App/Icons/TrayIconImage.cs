using System.IO;
using System.Runtime.InteropServices;
using Soundboard.Core.Design;

namespace Soundboard.App.Icons;

/// <summary>
/// The tray symbol: the app icon in the current accent, drawn at the small-icon size Windows
/// uses for the notification area (16 px at 100 %, 20 at 125 %, 24 at 150 % …).
/// </summary>
public static class TrayIconImage
{
    /// <summary>
    /// A new icon of exactly size × size pixels. The caller owns it (and its HICON):
    /// dispose it, or hand it to the tray, which disposes the previous one on replacement.
    /// Needs an STA thread.
    /// </summary>
    public static System.Drawing.Icon Create(Rgb accent, int size)
    {
        var ico = IcoFile.Build(accent, [size]);
        // Icon(Stream) copies the data; the stream may go away right after.
        using var ms = new MemoryStream(ico, writable: false);
        return new System.Drawing.Icon(ms, size, size);
    }

    /// <summary>Small-icon size for the DPI of the taskbar (16 if Windows cannot tell).</summary>
    public static int CurrentSize()
    {
        try
        {
            uint dpi = TaskbarDpi();
            int size = GetSystemMetricsForDpi(SmCxSmIcon, dpi);
            return size is >= 16 and <= 256 ? size : 16;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return 16;
        }
    }

    /// <summary>
    /// DPI of the monitor with the main taskbar as it is now; the system DPI as fallback.
    /// The app is only system DPI aware, which would freeze both values at sign-in, so the
    /// query runs with the thread switched to per-monitor awareness for a moment.
    /// </summary>
    private static uint TaskbarDpi()
    {
        IntPtr previous = IntPtr.Zero;
        try { previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2); }
        catch (EntryPointNotFoundException) { }
        try
        {
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(taskbar, MonitorDefaultToPrimary);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint x, out _) == 0 && x > 0)
                    return x;
            }
            uint system = GetDpiForSystem();
            return system > 0 ? system : 96;
        }
        finally
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private const int SmCxSmIcon = 49;
    private const uint MonitorDefaultToPrimary = 1;
    private const int MdtEffectiveDpi = 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
