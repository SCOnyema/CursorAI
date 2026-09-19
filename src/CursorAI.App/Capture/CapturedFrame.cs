using System.Windows.Media.Imaging;

namespace CursorAI.App.Capture;

public sealed class CapturedFrame
{
    internal CapturedFrame(
        BitmapSource image,
        DateTimeOffset capturedAt,
        PhysicalScreenBounds monitorBounds)
    {
        Image = image;
        PixelWidth = image.PixelWidth;
        PixelHeight = image.PixelHeight;
        CapturedAt = capturedAt;
        MonitorBounds = monitorBounds;
    }

    public BitmapSource Image { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public DateTimeOffset CapturedAt { get; }

    public PhysicalScreenBounds MonitorBounds { get; }
}
