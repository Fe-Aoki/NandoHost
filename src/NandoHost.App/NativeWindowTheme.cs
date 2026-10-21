using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static NandoHost.Core.Localizer;

namespace NandoHost.App;

internal static class NativeWindowTheme
{
    internal static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WorkAreaHook);
            var enabled = 1;
            // Unsupported DWM attributes return an HRESULT; they do not prevent
            // the WindowChrome client frame from working on older Windows 10.
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            var border = -2; // DWMWA_COLOR_NONE: suppress the native outer border.
            var caption = 0x00262120; // COLORREF for #202126
            DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        };
    }

    // Borderless windows must maximize to this monitor's work area, including
    // monitors above/left of the primary display and a taskbar on any edge.
    private static IntPtr WorkAreaHook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0024) return IntPtr.Zero; // WM_GETMINMAXINFO
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
        var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        limits.MaxPosition = new NativePoint { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
        limits.MaxSize = new NativePoint { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
        Marshal.StructureToPtr(limits, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    internal static bool MaximizedWithinWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetWindowRect(handle, out var rect) && GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)
            && rect.Left >= info.Work.Left && rect.Top >= info.Work.Top
            && rect.Right <= info.Work.Right && rect.Bottom <= info.Work.Bottom;
    }

    internal static int ClientTopInset(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var outer)) throw new InvalidOperationException(Tr("Não foi possível verificar a moldura da janela."));
        var client = new NativePoint();
        if (!ClientToScreen(handle, ref client)) throw new InvalidOperationException(Tr("Não foi possível verificar a área de desenho da janela."));
        return client.Y - outer.Top;
    }

    internal static int? BorderColor(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return DwmGetWindowAttribute(handle, 34, out var value, sizeof(int)) == 0 ? value : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
}
