using System.IO;

namespace CursorAI.App.Speech;

/// <summary>
/// Finds the Whisper model file, and explains precisely what to do when it is absent.
/// </summary>
/// <remarks>
/// Nothing is ever downloaded automatically. A speech model is hundreds of megabytes and
/// fetching that silently behind a hotkey press is not a decision software should make for
/// someone. A missing model therefore degrades to the legacy recognizer with an explanation,
/// rather than stalling or failing.
///
/// Models live under %LOCALAPPDATA%\CursorAI\models, never in the repository or the current
/// directory: an installed build may sit somewhere unwritable, and the current directory
/// depends on how the process happened to be launched.
///
/// Sizes verified by HTTP HEAD against the upstream repository on 2026-10-14:
/// tiny.en 74 MB, base.en 141 MB, small.en 465 MB, medium.en 1463 MB.
/// </remarks>
internal static class WhisperModelLocator
{
    internal const string ModelEnvironmentVariable = "CURSORAI_WHISPER_MODEL";
    internal const string DefaultModelName = "small.en";
    internal const string ModelBaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>Where models are expected: %LOCALAPPDATA%\CursorAI\models.</summary>
    internal static string ModelDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorAI",
        "models");

    /// <summary>
    /// Resolves the configured model name. Accepts a bare name such as "base.en", a file name
    /// such as "ggml-base.en.bin", or an absolute path.
    /// </summary>
    internal static string ResolveModelName(string? configured)
    {
        return string.IsNullOrWhiteSpace(configured) ? DefaultModelName : configured.Trim();
    }

    /// <summary>
    /// Returns the full path for a configured model, whether or not the file exists.
    /// </summary>
    internal static string ResolveModelPath(string? configured, string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        string name = ResolveModelName(configured);

        if (Path.IsPathRooted(name))
        {
            return name;
        }

        string fileName = name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"ggml-{name}.bin";

        return Path.Combine(modelDirectory, fileName);
    }

    /// <summary>
    /// Instructions for a missing model, naming the exact file, the exact folder, and the
    /// command to fetch it. A message that only says "model not found" is not actionable.
    /// </summary>
    internal static string DescribeMissingModel(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        string fileName = Path.GetFileName(modelPath);
        string directory = Path.GetDirectoryName(modelPath) ?? ModelDirectory;

        return $"Whisper model '{fileName}' was not found in {directory}, so CursorAI is using "
            + "the legacy Windows recognizer instead."
            + Environment.NewLine
            + $"To enable Whisper, download it and restart:"
            + Environment.NewLine
            + $"  curl -L -o \"{modelPath}\" {ModelBaseUrl}{fileName}"
            + Environment.NewLine
            + $"Set {ModelEnvironmentVariable} to choose another model "
            + "(tiny.en 74 MB, base.en 141 MB, small.en 465 MB, medium.en 1463 MB). "
            + "Nothing is downloaded automatically.";
    }
}
