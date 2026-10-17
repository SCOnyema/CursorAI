using System.Diagnostics;
using System.IO;
using System.Reflection;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace CursorAI.App.Speech;

/// <summary>
/// Makes Whisper's native library loadable from a single-file published build.
/// </summary>
/// <remarks>
/// Whisper.net looks for its native DLLs in <c>runtimes\win-x64</c> beneath the application
/// directory. A normal build puts them there, so voice worked under <c>dotnet run</c>. A
/// single-file build bundles them inside the executable and the .NET host extracts them to a
/// hashed folder under %TEMP% that Whisper.net never searches, so every published voice turn
/// failed with "Native Library not found in default paths" after the key was released.
///
/// The published build therefore embeds the four win-x64 DLLs as resources, and this writes
/// them to %LOCALAPPDATA%\CursorAI\runtimes on first use. Whisper.net searches
/// <c>runtimes\win-x64</c> under the parent directory of <see cref="RuntimeOptions.LibraryPath"/>,
/// not the path itself, which is why it is pointed at a file inside the root rather than at the
/// DLL. Verified 2026-10-17 against a single-file probe: pointing it at the DLL, or at either
/// directory, still failed; a file inside the root loaded the CPU runtime.
/// </remarks>
internal static class WhisperNativeLibrary
{
    internal const string ResourcePrefix = "WhisperNative.";

    private static readonly object Gate = new();
    private static bool _isPrepared;

    /// <summary>
    /// Prepares and loads the native library, throwing when it cannot be loaded, so a broken
    /// install is found at startup and falls back to the Windows recognizer rather than failing
    /// in the middle of someone's question.
    /// </summary>
    internal static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (!_isPrepared)
            {
                Prepare(
                    AppDomain.CurrentDomain.BaseDirectory,
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CursorAI",
                        "runtimes",
                        $"whisper-{typeof(WhisperFactory).Assembly.GetName().Version}"),
                    typeof(WhisperNativeLibrary).Assembly);
                _isPrepared = true;
            }
        }

        // Loads the native library without a model, so this costs milliseconds rather than the
        // seconds a model load takes.
        _ = WhisperFactory.GetRuntimeInfo();
    }

    /// <summary>
    /// Writes the embedded DLLs beneath <paramref name="extractionRoot"/> and points Whisper.net
    /// at them. Does nothing when the application directory already has them, as a normal
    /// build does, or when nothing is embedded.
    /// </summary>
    /// <returns>The directory the DLLs were written to, or null when none was needed.</returns>
    internal static string? Prepare(string applicationDirectory, string extractionRoot, Assembly source)
    {
        if (File.Exists(Path.Combine(applicationDirectory, "runtimes", "win-x64", "whisper.dll")))
        {
            return null;
        }

        string[] resources =
        [
            .. source.GetManifestResourceNames()
                .Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        ];
        if (resources.Length == 0)
        {
            return null;
        }

        string directory = Path.Combine(extractionRoot, "runtimes", "win-x64");
        Directory.CreateDirectory(directory);

        foreach (string resource in resources)
        {
            using Stream input = source.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded resource {resource} could not be opened.");
            string target = Path.Combine(directory, resource[ResourcePrefix.Length..]);

            // The folder is versioned, so a file of the right size is this file. Rewriting it
            // every start would fail anyway once a DLL is loaded and locked.
            if (File.Exists(target) && new FileInfo(target).Length == input.Length)
            {
                continue;
            }

            string temporary = target + ".tmp";
            using (FileStream output = File.Create(temporary))
            {
                input.CopyTo(output);
            }

            File.Move(temporary, target, overwrite: true);
        }

        RuntimeOptions.LibraryPath = Path.Combine(extractionRoot, "whisper.dll");
        Debug.WriteLine($"Whisper native libraries prepared in {directory}");
        return directory;
    }
}
