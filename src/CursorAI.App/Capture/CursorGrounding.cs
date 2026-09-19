namespace CursorAI.App.Capture;

internal static class CursorGrounding
{
    internal static PhysicalScreenPoint ToCaptureCoordinates(
        PhysicalScreenPoint screenPosition,
        PhysicalScreenBounds monitorBounds)
    {
        return new PhysicalScreenPoint(
            screenPosition.X - monitorBounds.X,
            screenPosition.Y - monitorBounds.Y);
    }
}
