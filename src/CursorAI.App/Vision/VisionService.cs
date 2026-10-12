using System.Diagnostics;
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

    public async Task<VisionResult> AnalyzeAsync(
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

        // Latency is measured here, not in a provider, so it means the same thing for all of
        // them: everything the user waits through after the frame is captured.
        long startedAt = Stopwatch.GetTimestamp();
        VisionResult result = await _provider.AnalyzeAsync(request, cancellationToken);
        return result with { Latency = Stopwatch.GetElapsedTime(startedAt) };
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
