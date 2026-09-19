using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace CursorAI.App.Capture;

internal static class DevelopmentCaptureStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static DevelopmentCaptureArtifacts Save(CapturedFrame frame, string repositoryRoot)
    {
        string captureDirectory = Path.Combine(repositoryRoot, "artifacts", "captures");
        Directory.CreateDirectory(captureDirectory);

        string baseName = CreateBaseFileName(frame.CapturedAt);
        string pngPath = Path.Combine(captureDirectory, $"{baseName}.png");
        string jsonPath = Path.Combine(captureDirectory, $"{baseName}.json");

        SavePng(frame, pngPath);
        SaveMetadata(frame, jsonPath);

        return new DevelopmentCaptureArtifacts(Path.GetFullPath(pngPath), Path.GetFullPath(jsonPath));
    }

    internal static string CreateFileName(DateTimeOffset timestamp) => $"{CreateBaseFileName(timestamp)}.png";

    internal static string CreateBaseFileName(DateTimeOffset timestamp)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"capture-{timestamp:yyyyMMdd-HHmmss-fff}");
    }

    private static void SavePng(CapturedFrame frame, string path)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(frame.Image));

        using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static void SaveMetadata(CapturedFrame frame, string path)
    {
        var metadata = new
        {
            frame.CapturedAt,
            Monitor = new
            {
                frame.Monitor.Bounds,
                frame.Monitor.WorkArea,
                frame.Monitor.IsPrimary
            },
            Cursor = new
            {
                Screen = frame.CursorScreenPosition,
                RelativeToCapture = frame.CursorPositionInImage
            },
            ForegroundWindow = new
            {
                frame.ForegroundWindow.Title,
                frame.ForegroundWindow.ProcessName
            }
        };

        string json = JsonSerializer.Serialize(metadata, JsonOptions);
        File.WriteAllText(path, json);
    }
}
