using System.ComponentModel;
using System.Runtime.InteropServices;
using Flow.Windows.Shell;
using Windows.Graphics;

namespace Flow.Windows;

internal sealed class NativeWindowSizing : IDisposable
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private static readonly UIntPtr SubclassId = new(0x464C4F57);
    private readonly IntPtr _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private bool _attached;

    public NativeWindowSizing(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("The window handle cannot be zero.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _subclassProcedure = WindowSubclassProcedure;
    }

    public SizeInt32 AttachAndGetInitialSize()
    {
        if (!SetWindowSubclass(_windowHandle, _subclassProcedure, SubclassId, UIntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the window sizing policy.");
        }

        _attached = true;
        if (!TryGetWorkArea(_windowHandle, out var workArea))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not determine the monitor work area.");
        }

        var size = FlowWindowsWindowSizePolicy.GetInitialSize(
            GetDpiForWindow(_windowHandle),
            workArea.Width,
            workArea.Height);
        return new SizeInt32(size.Width, size.Height);
    }

    public void Dispose()
    {
        if (!_attached)
        {
            return;
        }

        RemoveWindowSubclass(_windowHandle, _subclassProcedure, SubclassId);
        _attached = false;
    }

    private IntPtr WindowSubclassProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        if (message == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            var dpi = GetDpiForWindow(windowHandle);
            if (dpi > 0 && TryGetWorkArea(windowHandle, out var workArea))
            {
                var size = FlowWindowsWindowSizePolicy.GetMinimumSize(
                    dpi,
                    workArea.Width,
                    workArea.Height);
                var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                minMaxInfo.MinimumTrackSize = new NativePoint(size.Width, size.Height);
                Marshal.StructureToPtr(minMaxInfo, lParam, false);
            }
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private static bool TryGetWorkArea(IntPtr windowHandle, out NativeSize workArea)
    {
        var monitor = MonitorFromWindow(windowHandle, 2);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            workArea = default;
            return false;
        }

        workArea = new NativeSize(
            monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top);
        return true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaximumSize;
        public NativePoint MaximumPosition;
        public NativePoint MinimumTrackSize;
        public NativePoint MaximumTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    private readonly record struct NativeSize(int Width, int Height);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProcedure subclassProcedure,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr windowHandle,
        SubclassProcedure subclassProcedure,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
