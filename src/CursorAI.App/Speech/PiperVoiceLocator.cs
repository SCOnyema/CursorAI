using System.IO;
using System.Text.Json;

namespace CursorAI.App.Speech;

/// <summary>
/// Finds the Piper binary and voice model, and explains what is missing when they are absent.
/// </summary>
/// <remarks>
/// Why Piper, and why supplied rather than bundled.
///
/// Windows.Media.SpeechSynthesis was evaluated first and rejected on measurement: on the
/// development machine it exposes only Microsoft David, Zira and Mark - the same MSTTS_V110
/// engine System.Speech already uses - and zero "Natural" voices, so switching API would have
/// changed nothing audible. Kokoro-82M is Apache-2.0 for code and weights and sounds better
/// still, but needs ONNX Runtime plus a phonemizer in .NET, and its usual phonemizer
/// (espeak-ng) is itself GPL-3.0, so it costs far more work and reintroduces the licence
/// question it would be chosen to avoid.
///
/// Piper is invoked as a separate process whose files the user supplies, so CursorAI
/// distributes no Piper code and no GPL code. That matters because the maintained repository
/// (OHF-Voice/piper1-gpl) is now GPL-3.0 and ships only a Python wheel for Windows, while the
/// archived MIT release ships a standalone Windows binary. The standalone MIT binary is what
/// these instructions point at: no Python dependency and no copyleft entanglement. Voices in
/// rhasspy/piper-voices are MIT.
///
/// As with Whisper models, nothing is ever downloaded automatically.
/// </remarks>
internal static class PiperVoiceLocator
{
    internal const string ExecutableEnvironmentVariable = "CURSORAI_PIPER_EXE";
    internal const string VoiceEnvironmentVariable = "CURSORAI_PIPER_VOICE";
    internal const string DefaultVoiceName = "en_US-lessac-medium";

    /// <summary>Default location: %LOCALAPPDATA%\CursorAI\tts\piper.</summary>
    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorAI",
        "tts",
        "piper");

    internal static string ResolveExecutablePath(string? configured, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(directory, "piper.exe")
            : configured.Trim();
    }

    internal static string ResolveVoicePath(string? configured, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string name = string.IsNullOrWhiteSpace(configured) ? DefaultVoiceName : configured.Trim();

        if (Path.IsPathRooted(name))
        {
            return name;
        }

        string fileName = name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}.onnx";

        return Path.Combine(directory, fileName);
    }

    /// <summary>
    /// Reads the voice's sample rate from its companion .onnx.json. Piper streams raw PCM with
    /// no header, so playback has to be told the rate; guessing it wrong plays the answer at
    /// the wrong pitch rather than failing outright.
    /// </summary>
    internal static int ReadSampleRate(string voicePath, int fallbackSampleRate = 22050)
    {
        string configPath = voicePath + ".json";
        if (!File.Exists(configPath))
        {
            return fallbackSampleRate;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("audio", out JsonElement audio)
                && audio.TryGetProperty("sample_rate", out JsonElement rate)
                && rate.TryGetInt32(out int value)
                && value > 0)
            {
                return value;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
        }

        return fallbackSampleRate;
    }

    /// <summary>
    /// Instructions naming the exact files, folder and commands. A message that only says
    /// "Piper not found" is not actionable.
    /// </summary>
    internal static string DescribeMissing(string executablePath, string voicePath)
    {
        string directory = Path.GetDirectoryName(executablePath) ?? DefaultDirectory;
        string voiceName = Path.GetFileNameWithoutExtension(voicePath);

        return "Piper was not found, so CursorAI is using the built-in Windows voice."
            + Environment.NewLine
            + $"Expected {Path.GetFileName(executablePath)} and {Path.GetFileName(voicePath)} in {directory}."
            + Environment.NewLine
            + "To enable the better local voice, extract the MIT-licensed Windows build of Piper "
            + "there and add a voice, then restart:"
            + Environment.NewLine
            + "  https://github.com/rhasspy/piper/releases (piper_windows_amd64.zip, about 21 MB)"
            + Environment.NewLine
            + $"  https://huggingface.co/rhasspy/piper-voices (en/en_US/lessac/medium/{voiceName}.onnx"
            + " and .onnx.json, about 60 MB)"
            + Environment.NewLine
            + $"Set {ExecutableEnvironmentVariable} or {VoiceEnvironmentVariable} to use other "
            + "locations. Nothing is downloaded automatically.";
    }
}
