using System.IO;
using CursorAI.App.Speech;

namespace CursorAI.Tests;

public class WhisperNativeLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "CursorAI.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Prepare_WhenTheApplicationDirectoryHasTheLibrary_LeavesWhisperAlone()
    {
        // A normal build copies the DLLs beside the application, and Whisper.net finds them.
        string application = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(application, "runtimes", "win-x64"));
        File.WriteAllText(Path.Combine(application, "runtimes", "win-x64", "whisper.dll"), "x");

        string? prepared = WhisperNativeLibrary.Prepare(
            application, Path.Combine(_root, "extract"), typeof(WhisperNativeLibrary).Assembly);

        Assert.Null(prepared);
        Assert.False(Directory.Exists(Path.Combine(_root, "extract")));
    }

    [Fact]
    public void Prepare_WithNothingEmbedded_DoesNothing()
    {
        // The test build is not the published one, so nothing is embedded. Whisper.net then
        // reports its own failure, which startup turns into the legacy-recognizer fallback.
        string? prepared = WhisperNativeLibrary.Prepare(
            Path.Combine(_root, "empty-app"),
            Path.Combine(_root, "extract"),
            typeof(WhisperNativeLibraryTests).Assembly);

        Assert.Null(prepared);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
