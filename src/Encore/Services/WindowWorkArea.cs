using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Encore.Services;

internal static class WindowWorkArea
{
    internal static void Attach(Window window)
    {
        HwndSource? source = null;
        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(ConstrainMaximizedBounds);
        };
        window.Closed += (_, _) =>
        {
            if (source is { IsDisposed: false }) source.RemoveHook(ConstrainMaximizedBounds);
        };
    }

    private static nint ConstrainMaximizedBounds(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int getMinMaxInfo = 0x0024;
        const uint nearestMonitor = 2;
        if (message != getMinMaxInfo || lParam == 0) return 0;

        var monitor = MonitorFromWindow(window, nearestMonitor);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return 0;

        // A borderless window otherwise maximizes beyond the work area. These
        // Win32 rectangles are already in physical pixels for this monitor's DPI.
        var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        limits.MaxPosition = new NativePoint
        {
            X = info.Work.Left - info.Monitor.Left,
            Y = info.Work.Top - info.Monitor.Top
        };
        limits.MaxSize = new NativePoint
        {
            X = info.Work.Right - info.Work.Left,
            Y = info.Work.Bottom - info.Work.Top
        };
        Marshal.StructureToPtr(limits, lParam, false);

        // Allow WPF to process the message too, preserving its minimum resize
        // limits and its cached maximized layout size.
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
