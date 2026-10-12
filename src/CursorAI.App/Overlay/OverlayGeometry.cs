using System.Windows;
using CursorAI.App.Capture;

namespace CursorAI.App.Overlay;

/// <summary>
/// Converts coordinates in a captured screenshot into positions on an overlay window.
/// </summary>
/// <remarks>
/// Three coordinate spaces meet here and conflating any two produces guidance that points at
/// the wrong thing. A vision model answers in <em>image</em> pixels, measured from the
/// top-left of the screenshot. The screenshot covers one monitor, whose origin sits somewhere
/// in the virtual desktop and can be negative. WPF then positions and draws in
/// device-independent units, which on a 125% display are 0.8 physical pixels each.
///
/// Kept pure so all of it is testable: the development machine has a single display, so the
/// multi-monitor paths cannot be exercised by running the application.
/// </remarks>
internal static class OverlayGeometry
{
    /// <summary>
    /// Maps a rectangle in screenshot pixels to one in physical screen pixels.
    /// </summary>
    internal static PhysicalScreenBounds ToScreenBounds(
        PhysicalScreenBounds imageBounds,
        PhysicalScreenBounds monitorBounds)
    {
        return new PhysicalScreenBounds(
            monitorBounds.X + imageBounds.X,
            monitorBounds.Y + imageBounds.Y,
            imageBounds.Width,
            imageBounds.Height);
    }

    /// <summary>
    /// Maps a rectangle in screenshot pixels to one in device-independent units relative to an
    /// overlay window whose top-left is the monitor's top-left.
    /// </summary>
    /// <param name="dipsPerPixel">
    /// Device-independent units per physical pixel: 1.0 at 96 DPI, 0.8 at 120 DPI.
    /// </param>
    internal static Rect ToOverlayRect(PhysicalScreenBounds imageBounds, double dipsPerPixel)
    {
        if (dipsPerPixel <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dipsPerPixel), dipsPerPixel, "Scale must be positive.");
        }

        return new Rect(
            imageBounds.X * dipsPerPixel,
            imageBounds.Y * dipsPerPixel,
            imageBounds.Width * dipsPerPixel,
            imageBounds.Height * dipsPerPixel);
    }

    /// <summary>
    /// Clamps a rectangle to the captured image, so a model that over-reaches the screenshot
    /// cannot push guidance off-monitor. Returns null when nothing overlaps the image at all.
    /// </summary>
    internal static PhysicalScreenBounds? ClampToImage(
        PhysicalScreenBounds imageBounds,
        int imagePixelWidth,
        int imagePixelHeight)
    {
        int left = Math.Max(imageBounds.X, 0);
        int top = Math.Max(imageBounds.Y, 0);
        int right = Math.Min(imageBounds.X + imageBounds.Width, imagePixelWidth);
        int bottom = Math.Min(imageBounds.Y + imageBounds.Height, imagePixelHeight);

        return right <= left || bottom <= top
            ? null
            : new PhysicalScreenBounds(left, top, right - left, bottom - top);
    }
}
