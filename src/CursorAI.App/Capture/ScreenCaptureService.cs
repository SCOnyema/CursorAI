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
        if (!NativeMethods.GetCursorPos(out NativeMethods.NativePoint cursorPosition))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not determine the cursor position for capture.");
        }

        if (!NativeMethods.TryGetCursorMonitorBounds(cursorPosition, out NativeMethods.NativeRect nativeBounds))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not determine the monitor containing the cursor.");
        }

        int width = nativeBounds.Right - nativeBounds.Left;
        int height = nativeBounds.Bottom - nativeBounds.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"The capture monitor has invalid dimensions: {width}x{height}.");
        }

        BitmapSource image = CapturePhysicalRegion(nativeBounds.Left, nativeBounds.Top, width, height);
        PhysicalScreenBounds bounds = new(nativeBounds.Left, nativeBounds.Top, width, height);
        return new CapturedFrame(image, DateTimeOffset.Now, bounds);
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
