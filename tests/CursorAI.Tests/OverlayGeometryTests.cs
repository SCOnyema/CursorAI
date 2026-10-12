using System.Windows;
using CursorAI.App.Capture;
using CursorAI.App.Overlay;

namespace CursorAI.Tests;

public class OverlayGeometryTests
{
    [Fact]
    public void ToScreenBounds_OnThePrimaryMonitor_IsUnchanged()
    {
        PhysicalScreenBounds monitor = new(0, 0, 1920, 1080);
        PhysicalScreenBounds image = new(120, 300, 160, 48);

        PhysicalScreenBounds screen = OverlayGeometry.ToScreenBounds(image, monitor);

        Assert.Equal(new PhysicalScreenBounds(120, 300, 160, 48), screen);
    }

    [Fact]
    public void ToScreenBounds_OnAMonitorLeftOfPrimary_AppliesTheNegativeOrigin()
    {
        // The case the development machine cannot exercise: a second display placed to the
        // left of primary has a negative X origin.
        PhysicalScreenBounds monitor = new(-1920, 0, 1920, 1080);
        PhysicalScreenBounds image = new(100, 200, 50, 40);

        PhysicalScreenBounds screen = OverlayGeometry.ToScreenBounds(image, monitor);

        Assert.Equal(new PhysicalScreenBounds(-1820, 200, 50, 40), screen);
    }

    [Fact]
    public void ToScreenBounds_OnAMonitorAbovePrimary_AppliesTheNegativeOrigin()
    {
        PhysicalScreenBounds monitor = new(0, -1080, 1920, 1080);

        PhysicalScreenBounds screen = OverlayGeometry.ToScreenBounds(new(10, 20, 5, 5), monitor);

        Assert.Equal(new PhysicalScreenBounds(10, -1060, 5, 5), screen);
    }

    [Fact]
    public void ToOverlayRect_At96Dpi_IsOneToOne()
    {
        Rect rect = OverlayGeometry.ToOverlayRect(new(120, 300, 160, 48), dipsPerPixel: 1.0);

        Assert.Equal(new Rect(120, 300, 160, 48), rect);
    }

    [Fact]
    public void ToOverlayRect_At120Dpi_ShrinksByTheScaleFactor()
    {
        // This machine runs at 120 DPI, where one physical pixel is 0.8 device-independent
        // units. Treating them as equal would misplace guidance by a quarter of the screen.
        Rect rect = OverlayGeometry.ToOverlayRect(new(120, 300, 160, 48), dipsPerPixel: 0.8);

        // Component-wise with tolerance: Rect equality is exact, and 48 * 0.8 is
        // 38.400000000000006 in binary floating point.
        Assert.Equal(96, rect.X, precision: 9);
        Assert.Equal(240, rect.Y, precision: 9);
        Assert.Equal(128, rect.Width, precision: 9);
        Assert.Equal(38.4, rect.Height, precision: 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToOverlayRect_RejectsANonPositiveScale(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OverlayGeometry.ToOverlayRect(new(0, 0, 1, 1), scale));
    }

    [Fact]
    public void ClampToImage_LeavesAnInteriorRectangleAlone()
    {
        PhysicalScreenBounds? clamped = OverlayGeometry.ClampToImage(new(10, 10, 50, 50), 1920, 1080);

        Assert.Equal(new PhysicalScreenBounds(10, 10, 50, 50), clamped);
    }

    [Fact]
    public void ClampToImage_TrimsARectangleOverreachingTheImage()
    {
        // A model that guesses coordinates can exceed the screenshot; guidance must not be
        // pushed onto a neighbouring monitor as a result.
        PhysicalScreenBounds? clamped = OverlayGeometry.ClampToImage(new(1900, 1060, 200, 200), 1920, 1080);

        Assert.Equal(new PhysicalScreenBounds(1900, 1060, 20, 20), clamped);
    }

    [Fact]
    public void ClampToImage_TrimsNegativeOrigins()
    {
        PhysicalScreenBounds? clamped = OverlayGeometry.ClampToImage(new(-30, -10, 100, 50), 1920, 1080);

        Assert.Equal(new PhysicalScreenBounds(0, 0, 70, 40), clamped);
    }

    [Theory]
    [InlineData(2000, 100, 50, 50)]
    [InlineData(-100, 100, 50, 50)]
    [InlineData(10, 10, 0, 10)]
    public void ClampToImage_WhenNothingOverlaps_ReturnsNull(int x, int y, int width, int height)
    {
        Assert.Null(OverlayGeometry.ClampToImage(new(x, y, width, height), 1920, 1080));
    }
}
