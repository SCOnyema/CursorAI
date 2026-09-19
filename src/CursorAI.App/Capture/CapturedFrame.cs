using System.Windows.Media.Imaging;

namespace CursorAI.App.Capture;

public sealed class CapturedFrame
{
    internal CapturedFrame(
        BitmapSource image,
        DateTimeOffset capturedAt,
        CapturedMonitorInfo monitor,
        PhysicalScreenPoint cursorScreenPosition,
        PhysicalScreenPoint cursorPositionInImage,
        ForegroundWindowInfo foregroundWindow)
    {
        Image = image;
        PixelWidth = image.PixelWidth;
        PixelHeight = image.PixelHeight;
        CapturedAt = capturedAt;
        Monitor = monitor;
        CursorScreenPosition = cursorScreenPosition;
        CursorPositionInImage = cursorPositionInImage;
        ForegroundWindow = foregroundWindow;
    }

    public BitmapSource Image { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public DateTimeOffset CapturedAt { get; }

    public CapturedMonitorInfo Monitor { get; }

    public PhysicalScreenPoint CursorScreenPosition { get; }

    public PhysicalScreenPoint CursorPositionInImage { get; }

    public ForegroundWindowInfo ForegroundWindow { get; }
}
