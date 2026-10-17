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
        ForegroundWindowInfo foregroundWindow,
        bool requestsTargets = false,
        IReadOnlyList<ConversationTurn>? history = null)
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
        RequestsTargets = requestsTargets;
        History = history ?? [];
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

    /// <summary>
    /// True when the caller wants located elements to draw guidance on, not just prose.
    /// Measured 2026-10-12: asking for coordinates roughly triples latency, so it is opt-in.
    /// </summary>
    public bool RequestsTargets { get; }

    /// <summary>Earlier exchanges in this conversation, oldest first; empty for a fresh one.</summary>
    public IReadOnlyList<ConversationTurn> History { get; }
}
