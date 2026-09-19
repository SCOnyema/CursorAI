using System.IO;
using System.Windows.Media.Imaging;
using CursorAI.App.Capture;

namespace CursorAI.App.Vision;

public sealed class VisionService
{
    private readonly IVisionProvider _provider;

    public VisionService(IVisionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<VisionResult> AnalyzeAsync(
        string userPrompt,
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] pngData = EncodePng(frame);
        VisionRequest request = new(
            userPrompt,
            pngData,
            frame.PixelWidth,
            frame.PixelHeight,
            frame.CapturedAt,
            frame.Monitor,
            frame.CursorScreenPosition,
            frame.CursorPositionInImage,
            frame.ForegroundWindow);

        return _provider.AnalyzeAsync(request, cancellationToken);
    }

    private static byte[] EncodePng(CapturedFrame frame)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(frame.Image));

        using MemoryStream output = new();
        encoder.Save(output);
        return output.ToArray();
    }
}
