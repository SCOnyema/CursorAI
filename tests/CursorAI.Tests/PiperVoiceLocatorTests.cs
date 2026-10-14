using System.IO;
using CursorAI.App.Speech;

namespace CursorAI.Tests;

public class PiperVoiceLocatorTests : IDisposable
{
    private const string Directory = @"C:\tts\piper";

    private readonly string _temp = Path.Combine(
        Path.GetTempPath(), "CursorAI.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveExecutablePath_DefaultsToPiperExeInTheVoiceDirectory()
    {
        Assert.Equal(
            Path.Combine(Directory, "piper.exe"),
            PiperVoiceLocator.ResolveExecutablePath(null, Directory));
    }

    [Fact]
    public void ResolveExecutablePath_AcceptsAnExplicitPath()
    {
        const string explicitPath = @"D:\tools\piper\piper.exe";

        Assert.Equal(explicitPath, PiperVoiceLocator.ResolveExecutablePath(explicitPath, Directory));
    }

    [Fact]
    public void ResolveVoicePath_ExpandsABareVoiceNameToOnnx()
    {
        Assert.Equal(
            Path.Combine(Directory, "en_US-lessac-medium.onnx"),
            PiperVoiceLocator.ResolveVoicePath("en_US-lessac-medium", Directory));
    }

    [Fact]
    public void ResolveVoicePath_DefaultsToTheDocumentedVoice()
    {
        Assert.Equal(
            Path.Combine(Directory, "en_US-lessac-medium.onnx"),
            PiperVoiceLocator.ResolveVoicePath(null, Directory));
    }

    [Fact]
    public void ResolveVoicePath_AcceptsAFileNameOrAbsolutePath()
    {
        Assert.Equal(
            Path.Combine(Directory, "custom.onnx"),
            PiperVoiceLocator.ResolveVoicePath("custom.onnx", Directory));
        Assert.Equal(
            @"E:\voices\other.onnx",
            PiperVoiceLocator.ResolveVoicePath(@"E:\voices\other.onnx", Directory));
    }

    [Fact]
    public void DefaultDirectory_IsUnderLocalAppData()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, PiperVoiceLocator.DefaultDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("CursorAI", "tts", "piper"), PiperVoiceLocator.DefaultDirectory);
    }

    [Fact]
    public void ReadSampleRate_ReadsTheRateFromTheCompanionJson()
    {
        // Piper streams headerless PCM, so playback must be told the rate; guessing wrong plays
        // the answer at the wrong pitch rather than failing.
        System.IO.Directory.CreateDirectory(_temp);
        string voice = Path.Combine(_temp, "voice.onnx");
        File.WriteAllText(voice, "not a real model");
        File.WriteAllText(voice + ".json", """{"audio":{"sample_rate":16000},"other":1}""");

        Assert.Equal(16000, PiperVoiceLocator.ReadSampleRate(voice));
    }

    [Fact]
    public void ReadSampleRate_WithNoJson_UsesTheFallback()
    {
        System.IO.Directory.CreateDirectory(_temp);
        string voice = Path.Combine(_temp, "bare.onnx");
        File.WriteAllText(voice, "x");

        Assert.Equal(22050, PiperVoiceLocator.ReadSampleRate(voice));
    }

    [Fact]
    public void ReadSampleRate_WithCorruptOrMissingFields_UsesTheFallback()
    {
        System.IO.Directory.CreateDirectory(_temp);
        string bad = Path.Combine(_temp, "bad.onnx");
        File.WriteAllText(bad, "x");
        File.WriteAllText(bad + ".json", "{ not json");
        Assert.Equal(22050, PiperVoiceLocator.ReadSampleRate(bad));

        string partial = Path.Combine(_temp, "partial.onnx");
        File.WriteAllText(partial, "x");
        File.WriteAllText(partial + ".json", """{"audio":{}}""");
        Assert.Equal(22050, PiperVoiceLocator.ReadSampleRate(partial));
    }

    [Fact]
    public void DescribeMissing_NamesTheFilesFolderAndWhereToGetThem()
    {
        string message = PiperVoiceLocator.DescribeMissing(
            Path.Combine(Directory, "piper.exe"),
            Path.Combine(Directory, "en_US-lessac-medium.onnx"));

        Assert.Contains("piper.exe", message);
        Assert.Contains("en_US-lessac-medium.onnx", message);
        Assert.Contains(Directory, message);
        Assert.Contains("built-in Windows voice", message);
        Assert.Contains("piper_windows_amd64.zip", message);
        Assert.Contains("piper-voices", message);
        Assert.Contains("Nothing is downloaded automatically", message);
    }

    [Fact]
    public void DescribeMissing_PointsAtTheMitBuild_NotTheGplOne()
    {
        // The maintained repository is GPL-3.0 and ships only a Python wheel for Windows; the
        // archived MIT release ships a standalone binary. CursorAI distributes neither, but the
        // instructions must not send someone to the copyleft one.
        string message = PiperVoiceLocator.DescribeMissing(
            Path.Combine(Directory, "piper.exe"),
            Path.Combine(Directory, "voice.onnx"));

        Assert.Contains("MIT-licensed", message);
        Assert.Contains("rhasspy/piper/releases", message);
        Assert.DoesNotContain("piper1-gpl", message);
    }

    [Fact]
    public void PiperSpeechSynthesizer_WithMissingFiles_ThrowsTheActionableMessage()
    {
        string absent = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.exe");

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
            () => new PiperSpeechSynthesizer(absent, absent + ".onnx"));

        Assert.Contains("built-in Windows voice", exception.Message);
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(_temp))
            {
                System.IO.Directory.Delete(_temp, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
