using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;

namespace CursorAI.App.Capture;

internal static class DevelopmentCaptureStorage
{
    internal static string SavePng(CapturedFrame frame, string repositoryRoot)
    {
        string captureDirectory = Path.Combine(repositoryRoot, "artifacts", "captures");
        Directory.CreateDirectory(captureDirectory);

        string path = Path.Combine(captureDirectory, CreateFileName(frame.CapturedAt));
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(frame.Image));

        using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
        return Path.GetFullPath(path);
    }

    internal static string CreateFileName(DateTimeOffset timestamp)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"capture-{timestamp:yyyyMMdd-HHmmss-fff}.png");
    }
}
