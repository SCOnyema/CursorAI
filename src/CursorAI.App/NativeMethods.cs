using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CursorAI.App;

internal static class NativeMethods
{
    private const int ExtendedWindowStyleIndex = -20;
    private const long TransparentExtendedStyle = 0x00000020L;
    private const long NoActivateExtendedStyle = 0x08000000L;
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    internal static void EnableClickThrough(nint windowHandle)
    {
        Marshal.SetLastPInvokeError(0);
        nint currentStyles = GetWindowLongPtr(windowHandle, ExtendedWindowStyleIndex);
        int error = Marshal.GetLastPInvokeError();
        if (currentStyles == 0 && error != 0)
        {
            throw new Win32Exception(error, "Could not read the buddy window's extended styles.");
        }

        nint clickThroughStyles = currentStyles | (nint)(TransparentExtendedStyle | NoActivateExtendedStyle);

        Marshal.SetLastPInvokeError(0);
        nint previousStyles = SetWindowLongPtr(windowHandle, ExtendedWindowStyleIndex, clickThroughStyles);
        error = Marshal.GetLastPInvokeError();
        if (previousStyles == 0 && error != 0)
        {
            throw new Win32Exception(error, "Could not enable click-through behavior for the buddy window.");
        }
    }

    internal static bool TryGetCursorWorkArea(NativePoint cursorPosition, out NativeRect workArea)
    {
        nint monitor = MonitorFromPoint(cursorPosition, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        bool succeeded = monitor != 0 && GetMonitorInfo(monitor, ref monitorInfo);
        workArea = monitorInfo.WorkArea;
        return succeeded;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal int Size;
        internal NativeRect MonitorArea;
        internal NativeRect WorkArea;
        internal uint Flags;
    }
}
