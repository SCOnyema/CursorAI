using System.Windows.Media;
using System.Windows.Media.Imaging;
using CursorAI.App.Capture;
using CursorAI.App.Vision;

namespace CursorAI.Tests;

public class VisionServiceTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public async Task AnalyzeAsync_ForwardsProviderNeutralSnapshotAndReturnsProviderResult()
    {
        DateTimeOffset capturedAt = new(2026, 9, 19, 14, 30, 0, TimeSpan.FromHours(2));
        CapturedMonitorInfo monitor = new(
            new PhysicalScreenBounds(-1920, 0, 1920, 1080),
            new PhysicalScreenBounds(-1920, 0, 1920, 1040),
            false);
        PhysicalScreenPoint cursorScreen = new(-1200, 400);
        PhysicalScreenPoint cursorInImage = new(720, 400);
        ForegroundWindowInfo foreground = new(123, "Design document", "editor");
        CapturedFrame frame = CreateFrame(capturedAt, monitor, cursorScreen, cursorInImage, foreground);
        VisionResult expectedResult = new("Provider response");
        RecordingVisionProvider provider = new(expectedResult);
        VisionService service = new(provider);
        using CancellationTokenSource cancellation = new();

        VisionResult result = await service.AnalyzeAsync("What is under my cursor?", frame, cancellation.Token);

        Assert.Same(expectedResult, result);
        Assert.NotNull(provider.Request);
        Assert.Equal("What is under my cursor?", provider.Request.UserPrompt);
        Assert.Equal("image/png", provider.Request.ImageMediaType);
        Assert.True(provider.Request.EncodedImage.Length > PngSignature.Length);
        Assert.Equal(PngSignature, provider.Request.EncodedImage.Span[..PngSignature.Length].ToArray());
        Assert.Equal(2, provider.Request.ImagePixelWidth);
        Assert.Equal(2, provider.Request.ImagePixelHeight);
        Assert.Equal(capturedAt, provider.Request.CapturedAt);
        Assert.Same(monitor, provider.Request.Monitor);
        Assert.Equal(cursorScreen, provider.Request.CursorScreenPosition);
        Assert.Equal(cursorInImage, provider.Request.CursorPositionInImage);
        Assert.Same(foreground, provider.Request.ForegroundWindow);
        Assert.Equal("Design document", provider.Request.ForegroundWindow.Title);
        Assert.Equal("editor", provider.Request.ForegroundWindow.ProcessName);
        Assert.Equal(cancellation.Token, provider.CancellationToken);
    }

    [Fact]
    public async Task AnalyzeAsync_PreservesUnavailableForegroundMetadata()
    {
        ForegroundWindowInfo foreground = new(0, null, null);
        CapturedFrame frame = CreateFrame(
            DateTimeOffset.UnixEpoch,
            new CapturedMonitorInfo(
                new PhysicalScreenBounds(0, 0, 2, 2),
                new PhysicalScreenBounds(0, 0, 2, 2),
                true),
            new PhysicalScreenPoint(0, 0),
            new PhysicalScreenPoint(0, 0),
            foreground);
        RecordingVisionProvider provider = new(new VisionResult("ok"));
        VisionService service = new(provider);

        await service.AnalyzeAsync("Describe this screen", frame);

        Assert.NotNull(provider.Request);
        Assert.Null(provider.Request.ForegroundWindow.Title);
        Assert.Null(provider.Request.ForegroundWindow.ProcessName);
    }

    private static CapturedFrame CreateFrame(
        DateTimeOffset capturedAt,
        CapturedMonitorInfo monitor,
        PhysicalScreenPoint cursorScreen,
        PhysicalScreenPoint cursorInImage,
        ForegroundWindowInfo foreground)
    {
        byte[] pixels =
        [
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF,
            0xFF, 0x00, 0x00, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        ];
        BitmapSource image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        image.Freeze();

        return new CapturedFrame(image, capturedAt, monitor, cursorScreen, cursorInImage, foreground);
    }

    private sealed class RecordingVisionProvider(VisionResult result) : IVisionProvider
    {
        internal VisionRequest? Request { get; private set; }

        internal CancellationToken CancellationToken { get; private set; }

        public Task<VisionResult> AnalyzeAsync(VisionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
