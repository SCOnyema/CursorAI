using System.IO;
using CursorAI.App.Speech;

namespace CursorAI.Tests;

public class WhisperModelLocatorTests
{
    private const string Directory = @"C:\models";

    [Fact]
    public void ResolveModelName_DefaultsToSmallEn()
    {
        Assert.Equal("small.en", WhisperModelLocator.ResolveModelName(null));
        Assert.Equal("small.en", WhisperModelLocator.ResolveModelName("   "));
        Assert.Equal("small.en", WhisperModelLocator.DefaultModelName);
    }

    [Fact]
    public void ResolveModelName_TrimsAConfiguredValue()
    {
        Assert.Equal("base.en", WhisperModelLocator.ResolveModelName("  base.en  "));
    }

    [Fact]
    public void ResolveModelPath_ExpandsABareNameToTheGgmlFileName()
    {
        string path = WhisperModelLocator.ResolveModelPath("base.en", Directory);

        Assert.Equal(Path.Combine(Directory, "ggml-base.en.bin"), path);
    }

    [Fact]
    public void ResolveModelPath_AcceptsAFullFileNameUnchanged()
    {
        string path = WhisperModelLocator.ResolveModelPath("ggml-tiny.en.bin", Directory);

        Assert.Equal(Path.Combine(Directory, "ggml-tiny.en.bin"), path);
    }

    [Fact]
    public void ResolveModelPath_AcceptsAnAbsolutePathAndIgnoresTheModelDirectory()
    {
        // Someone with the model already on disk should not have to move it.
        const string absolute = @"D:\whisper\my-model.bin";

        Assert.Equal(absolute, WhisperModelLocator.ResolveModelPath(absolute, Directory));
    }

    [Fact]
    public void ModelDirectory_IsUnderLocalAppData_NotTheRepoOrCurrentDirectory()
    {
        // The current directory depends on how the process was launched, and an installed build
        // may sit somewhere unwritable.
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, WhisperModelLocator.ModelDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("CursorAI", "models"), WhisperModelLocator.ModelDirectory);
    }

    [Fact]
    public void DescribeMissingModel_NamesTheFileTheFolderAndTheCommand()
    {
        // A message that only says "model not found" is not actionable, and nothing is ever
        // downloaded automatically, so the instructions have to be complete.
        string message = WhisperModelLocator.DescribeMissingModel(
            Path.Combine(Directory, "ggml-base.en.bin"));

        Assert.Contains("ggml-base.en.bin", message);
        Assert.Contains(Directory, message);
        Assert.Contains("curl", message);
        Assert.Contains(WhisperModelLocator.ModelBaseUrl, message);
        Assert.Contains("legacy Windows recognizer", message);
        Assert.Contains("Nothing is downloaded automatically", message);
    }

    [Fact]
    public void DescribeMissingModel_ListsTheVerifiedModelSizes()
    {
        // Measured by HTTP HEAD on 2026-10-14, not quoted from memory.
        string message = WhisperModelLocator.DescribeMissingModel(
            Path.Combine(Directory, "ggml-small.en.bin"));

        Assert.Contains("tiny.en 74 MB", message);
        Assert.Contains("base.en 141 MB", message);
        Assert.Contains("small.en 465 MB", message);
        Assert.Contains("medium.en 1463 MB", message);
    }

    [Fact]
    public void WhisperSpeechRecognizer_WithNoModelFile_ThrowsTheActionableMessage()
    {
        // Constructed eagerly so an unusable model is discovered at startup, where it can fall
        // back to the legacy engine, rather than on the first hotkey press.
        string missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.bin");

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
            () => new WhisperSpeechRecognizer(missing));

        Assert.Contains("curl", exception.Message);
        Assert.Contains(Path.GetFileName(missing), exception.Message);
    }
}
