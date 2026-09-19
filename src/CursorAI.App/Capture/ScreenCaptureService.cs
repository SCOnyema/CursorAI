using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace CursorAI.App.Capture;

internal sealed class ScreenCaptureService
{
    internal CapturedFrame CaptureMonitorContainingCursor()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.NativePoint nativeCursor))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not determine the cursor position for capture.");
        }

        if (!NativeMethods.TryGetCursorMonitorContext(nativeCursor, out NativeMethods.NativeMonitorContext nativeMonitor))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not determine the monitor containing the cursor.");
        }

        PhysicalScreenBounds monitorBounds = ToBounds(nativeMonitor.Bounds);
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
        {
            throw new InvalidOperationException(
                $"The capture monitor has invalid dimensions: {monitorBounds.Width}x{monitorBounds.Height}.");
        }

        PhysicalScreenPoint cursorScreenPosition = new(nativeCursor.X, nativeCursor.Y);
        PhysicalScreenPoint cursorPositionInImage = CursorGrounding.ToCaptureCoordinates(
            cursorScreenPosition,
            monitorBounds);
        CapturedMonitorInfo monitor = new(
            monitorBounds,
            ToBounds(nativeMonitor.WorkArea),
            nativeMonitor.IsPrimary);
        ForegroundWindowInfo foregroundWindow = CaptureForegroundWindowInfo();

        DateTimeOffset capturedAt = DateTimeOffset.Now;
        BitmapSource image = CapturePhysicalRegion(
            monitorBounds.X,
            monitorBounds.Y,
            monitorBounds.Width,
            monitorBounds.Height);

        return new CapturedFrame(
            image,
            capturedAt,
            monitor,
            cursorScreenPosition,
            cursorPositionInImage,
            foregroundWindow);
    }

    private static ForegroundWindowInfo CaptureForegroundWindowInfo()
    {
        nint windowHandle = NativeMethods.GetForegroundWindowHandle();
        if (windowHandle == 0)
        {
            return new ForegroundWindowInfo(0, null, null);
        }

        string? title = NativeMethods.TryGetWindowTitle(windowHandle);
        string? processName = null;

        uint processId = NativeMethods.GetWindowProcessId(windowHandle);
        if (processId != 0)
        {
            try
            {
                using Process process = Process.GetProcessById(checked((int)processId));
                processName = process.ProcessName;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or InvalidOperationException
                or Win32Exception
                or OverflowException)
            {
                Debug.WriteLine($"Could not resolve foreground process {processId}: {exception.Message}");
            }
        }

        return new ForegroundWindowInfo(windowHandle, title, processName);
    }

    private static PhysicalScreenBounds ToBounds(NativeMethods.NativeRect rectangle)
    {
        return new PhysicalScreenBounds(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
    }

    private static BitmapSource CapturePhysicalRegion(int x, int y, int width, int height)
    {
        nint screenDeviceContext = NativeMethods.GetDesktopDeviceContext();
        if (screenDeviceContext == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not acquire the desktop device context.");
        }

        nint memoryDeviceContext = 0;
        nint bitmap = 0;
        nint previousObject = 0;

        try
        {
            memoryDeviceContext = NativeMethods.CreateCompatibleDeviceContext(screenDeviceContext);
            if (memoryDeviceContext == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the capture device context.");
            }

            bitmap = NativeMethods.CreateCompatibleBitmap(screenDeviceContext, width, height);
            if (bitmap == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the capture bitmap.");
            }

            previousObject = NativeMethods.SelectGraphicsObject(memoryDeviceContext, bitmap);
            if (previousObject == 0 || previousObject == new nint(-1))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not select the bitmap into the capture device context.");
            }

            if (!NativeMethods.CopyScreenPixels(memoryDeviceContext, screenDeviceContext, x, y, width, height))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not copy pixels from the selected monitor.");
            }

            BitmapSource image = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            if (previousObject != 0 && previousObject != new nint(-1) && memoryDeviceContext != 0)
            {
                NativeMethods.SelectGraphicsObject(memoryDeviceContext, previousObject);
            }

            if (bitmap != 0 && !NativeMethods.DeleteGraphicsObject(bitmap))
            {
                Debug.WriteLine($"Could not release the capture bitmap. Win32 error: {Marshal.GetLastPInvokeError()}.");
            }

            if (memoryDeviceContext != 0 && !NativeMethods.DeleteDeviceContext(memoryDeviceContext))
            {
                Debug.WriteLine($"Could not release the capture device context. Win32 error: {Marshal.GetLastPInvokeError()}.");
            }

            if (NativeMethods.ReleaseDesktopDeviceContext(screenDeviceContext) == 0)
            {
                Debug.WriteLine($"Could not release the desktop device context. Win32 error: {Marshal.GetLastPInvokeError()}.");
            }
        }
    }
}
