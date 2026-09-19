using CursorAI.App.Capture;

namespace CursorAI.Tests;

public class CursorGroundingTests
{
    [Fact]
    public void ToCaptureCoordinates_OnPrimaryStyleMonitor_PreservesCoordinates()
    {
        PhysicalScreenPoint result = CursorGrounding.ToCaptureCoordinates(
            new PhysicalScreenPoint(500, 300),
            new PhysicalScreenBounds(0, 0, 1920, 1080));

        Assert.Equal(new PhysicalScreenPoint(500, 300), result);
    }

    [Fact]
    public void ToCaptureCoordinates_OnMonitorLeftOfPrimary_OffsetsNegativeOrigin()
    {
        PhysicalScreenPoint result = CursorGrounding.ToCaptureCoordinates(
            new PhysicalScreenPoint(-1200, 400),
            new PhysicalScreenBounds(-1920, 0, 1920, 1080));

        Assert.Equal(new PhysicalScreenPoint(720, 400), result);
    }

    [Fact]
    public void ToCaptureCoordinates_OnMonitorAbovePrimary_OffsetsNegativeOrigin()
    {
        PhysicalScreenPoint result = CursorGrounding.ToCaptureCoordinates(
            new PhysicalScreenPoint(600, -500),
            new PhysicalScreenBounds(0, -1080, 1920, 1080));

        Assert.Equal(new PhysicalScreenPoint(600, 580), result);
    }

    [Theory]
    [InlineData(-1920, -1080, 0, 0)]
    [InlineData(-1, -1, 1919, 1079)]
    public void ToCaptureCoordinates_AtMonitorBoundaries_MapsToImageBoundaries(
        int screenX,
        int screenY,
        int expectedX,
        int expectedY)
    {
        PhysicalScreenPoint result = CursorGrounding.ToCaptureCoordinates(
            new PhysicalScreenPoint(screenX, screenY),
            new PhysicalScreenBounds(-1920, -1080, 1920, 1080));

        Assert.Equal(new PhysicalScreenPoint(expectedX, expectedY), result);
    }
}
