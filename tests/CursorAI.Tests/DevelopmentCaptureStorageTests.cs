using CursorAI.App.Capture;

namespace CursorAI.Tests;

public class DevelopmentCaptureStorageTests
{
    [Fact]
    public void CreateFileName_UsesTimestampAndPngExtension()
    {
        DateTimeOffset timestamp = new(2026, 9, 19, 10, 45, 0, 123, TimeSpan.FromHours(2));

        string fileName = DevelopmentCaptureStorage.CreateFileName(timestamp);

        Assert.Equal("capture-20260919-104500-123.png", fileName);
    }
}
