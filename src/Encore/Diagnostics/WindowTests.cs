using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Encore.Diagnostics;

internal static class WindowTests
{
    internal static void AssertFitsWorkArea(Window window, params FrameworkElement[] controls)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetClientRect(handle, out var client))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var origin = new NativePoint();
        if (!ClientToScreen(handle, ref origin))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var workArea = new Rect(info.Work.Left, info.Work.Top,
            info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        var clientBounds = new Rect(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        AssertInside(clientBounds, workArea, "Maximized client area");
        if (Math.Abs(clientBounds.Width - workArea.Width) > 1 || Math.Abs(clientBounds.Height - workArea.Height) > 1)
            throw new Exception($"Maximized window does not fill the work area: {clientBounds}; available: {workArea}.");

        foreach (var control in controls)
        {
            var bounds = new Rect(control.PointToScreen(new Point()),
                control.PointToScreen(new Point(control.ActualWidth, control.ActualHeight)));
            AssertInside(bounds, workArea, control.Name);
        }
    }

    private static void AssertInside(Rect bounds, Rect workArea, string name)
    {
        const double tolerance = 1;
        if (bounds.Left < workArea.Left - tolerance || bounds.Top < workArea.Top - tolerance ||
            bounds.Right > workArea.Right + tolerance || bounds.Bottom > workArea.Bottom + tolerance)
            throw new Exception($"{name} extends outside the visible work area: {bounds}; available: {workArea}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);
}
