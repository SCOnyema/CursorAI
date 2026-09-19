using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CursorAI.App;

internal static class NativeMethods
{
    internal const int HotkeyMessage = 0x0312;

    private const int ExtendedWindowStyleIndex = -20;
    private const long TransparentExtendedStyle = 0x00000020L;
    private const long NoActivateExtendedStyle = 0x08000000L;
    private const uint AltHotkeyModifier = 0x0001;
    private const uint ControlHotkeyModifier = 0x0002;
    private const uint ShiftHotkeyModifier = 0x0004;
    private const uint NoRepeatHotkeyModifier = 0x4000;
    private const uint SpaceVirtualKey = 0x20;
    private const uint SKeyVirtualKey = 0x53;
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimaryFlag = 1;
    private const uint SourceCopyRasterOperation = 0x00CC0020;
    private const uint CaptureLayeredWindowsRasterOperation = 0x40000000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint windowHandle, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetDC", SetLastError = true)]
    private static extern nint GetDeviceContext(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC", SetLastError = true)]
    private static extern int ReleaseDeviceContext(nint windowHandle, nint deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC", SetLastError = true)]
    internal static extern nint CreateCompatibleDeviceContext(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

    [DllImport("gdi32.dll", EntryPoint = "SelectObject", SetLastError = true)]
    internal static extern nint SelectGraphicsObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteGraphicsObject(nint graphicsObject);

    [DllImport("gdi32.dll", EntryPoint = "DeleteDC", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDeviceContext(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint destinationDeviceContext,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint sourceDeviceContext,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    internal static void RegisterActivationHotkey(nint windowHandle, int id)
    {
        uint modifiers = ControlHotkeyModifier | AltHotkeyModifier | NoRepeatHotkeyModifier;
        RegisterApplicationHotkey(windowHandle, id, modifiers, SpaceVirtualKey, "Ctrl + Alt + Space");
    }

    internal static void RegisterCaptureHotkey(nint windowHandle, int id)
    {
        uint modifiers = ControlHotkeyModifier | AltHotkeyModifier | ShiftHotkeyModifier | NoRepeatHotkeyModifier;
        RegisterApplicationHotkey(windowHandle, id, modifiers, SKeyVirtualKey, "Ctrl + Alt + Shift + S");
    }

    internal static nint GetForegroundWindowHandle() => GetForegroundWindow();

    internal static uint GetWindowProcessId(nint windowHandle)
    {
        _ = GetWindowThreadProcessId(windowHandle, out uint processId);
        return processId;
    }

    internal static string? TryGetWindowTitle(nint windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return null;
        }

        StringBuilder text = new(length + 1);
        return GetWindowText(windowHandle, text, text.Capacity) > 0
            ? text.ToString()
            : null;
    }

    internal static nint GetDesktopDeviceContext() => GetDeviceContext(0);

    internal static int ReleaseDesktopDeviceContext(nint deviceContext) => ReleaseDeviceContext(0, deviceContext);

    internal static bool CopyScreenPixels(
        nint destinationDeviceContext,
        nint sourceDeviceContext,
        int sourceX,
        int sourceY,
        int width,
        int height)
    {
        return BitBlt(
            destinationDeviceContext,
            0,
            0,
            width,
            height,
            sourceDeviceContext,
            sourceX,
            sourceY,
            SourceCopyRasterOperation | CaptureLayeredWindowsRasterOperation);
    }

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
        bool succeeded = TryGetCursorMonitorContext(cursorPosition, out NativeMonitorContext monitor);
        workArea = monitor.WorkArea;
        return succeeded;
    }

    internal static bool TryGetCursorMonitorContext(
        NativePoint cursorPosition,
        out NativeMonitorContext monitorContext)
    {
        nint monitor = MonitorFromPoint(cursorPosition, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        bool succeeded = monitor != 0 && GetMonitorInfo(monitor, ref monitorInfo);
        monitorContext = new NativeMonitorContext(
            monitorInfo.MonitorArea,
            monitorInfo.WorkArea,
            (monitorInfo.Flags & MonitorInfoPrimaryFlag) != 0);
        return succeeded;
    }

    private static void RegisterApplicationHotkey(
        nint windowHandle,
        int id,
        uint modifiers,
        uint virtualKey,
        string displayName)
    {
        if (!RegisterHotKey(windowHandle, id, modifiers, virtualKey))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"Could not register {displayName}. Another application may already own this shortcut.");
        }
    }

    internal readonly record struct NativeMonitorContext(
        NativeRect Bounds,
        NativeRect WorkArea,
        bool IsPrimary);

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
