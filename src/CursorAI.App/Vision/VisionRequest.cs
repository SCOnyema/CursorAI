using CursorAI.App.Capture;

namespace CursorAI.App.Vision;

public sealed class VisionRequest
{
    internal VisionRequest(
        string userPrompt,
        ReadOnlyMemory<byte> encodedImage,
        int imagePixelWidth,
        int imagePixelHeight,
        DateTimeOffset capturedAt,
        CapturedMonitorInfo monitor,
        PhysicalScreenPoint cursorScreenPosition,
        PhysicalScreenPoint cursorPositionInImage,
        ForegroundWindowInfo foregroundWindow)
    {
        UserPrompt = userPrompt;
        EncodedImage = encodedImage;
        ImagePixelWidth = imagePixelWidth;
        ImagePixelHeight = imagePixelHeight;
        CapturedAt = capturedAt;
        Monitor = monitor;
        CursorScreenPosition = cursorScreenPosition;
        CursorPositionInImage = cursorPositionInImage;
        ForegroundWindow = foregroundWindow;
    }

    public string UserPrompt { get; }

    public ReadOnlyMemory<byte> EncodedImage { get; }

    public string ImageMediaType => "image/png";

    public int ImagePixelWidth { get; }

    public int ImagePixelHeight { get; }

    public DateTimeOffset CapturedAt { get; }

    public CapturedMonitorInfo Monitor { get; }

    public PhysicalScreenPoint CursorScreenPosition { get; }

    public PhysicalScreenPoint CursorPositionInImage { get; }

    public ForegroundWindowInfo ForegroundWindow { get; }
}
