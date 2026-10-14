using System.Diagnostics;
using System.IO;
using System.Text;

namespace CursorAI.App.Diagnostics;

/// <summary>
/// Mirrors everything written to the console into a dated log file.
/// </summary>
/// <remarks>
/// Until this existed, every diagnostic went to a console that only exists when CursorAI is
/// launched from a terminal. A tester running the executable produced no record at all, so
/// "it didn't work" was the most any bug report could ever contain.
///
/// Implemented by wrapping <see cref="Console.Out"/> and <see cref="Console.Error"/> rather
/// than by introducing a logging call at every site. That captures the existing diagnostics
/// exactly as they are, and cannot drift out of sync with them.
///
/// Logs go under %LOCALAPPDATA%, never beside the executable, because an installed build may
/// sit somewhere unwritable.
/// </remarks>
internal static class FileLog
{
    private const int RetainedDays = 7;

    private static StreamWriter? _writer;

    /// <summary>Where logs are written: %LOCALAPPDATA%\CursorAI\logs.</summary>
    internal static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorAI",
        "logs");

    /// <summary>The current log file, or null when logging could not be started.</summary>
    internal static string? CurrentPath { get; private set; }

    /// <summary>
    /// Starts mirroring console output to a file. Safe to call once; failures are reported to
    /// the console and otherwise ignored, because losing the log must not stop the application.
    /// </summary>
    internal static void Start()
    {
        if (_writer is not null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            string path = Path.Combine(
                LogDirectory,
                $"cursorai-{DateTime.Now:yyyyMMdd}.log");

            // Appended, so several runs on the same day accumulate rather than overwrite:
            // a crash followed by a relaunch is exactly when the earlier run matters most.
            // No byte-order mark: the file is appended to across runs and tools that tail logs
            // should not meet one mid-file.
            StreamWriter writer = new(
                path,
                append: true,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            _writer = writer;
            CurrentPath = path;

            Console.SetOut(new TeeTextWriter(Console.Out, writer, "   "));
            Console.SetError(new TeeTextWriter(Console.Error, writer, "ERR"));

            Console.WriteLine(
                $"--- CursorAI started {DateTime.Now:yyyy-MM-dd HH:mm:ss} --- log: {path}");
            PruneOldLogs();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not start file logging: {exception.Message}");
        }
    }

    internal static void Stop()
    {
        StreamWriter? writer = _writer;
        _writer = null;

        if (writer is null)
        {
            return;
        }

        try
        {
            writer.WriteLine($"--- CursorAI exited {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
            writer.Flush();
            writer.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Debug.WriteLine($"Could not close the log: {exception.Message}");
        }
    }

    /// <summary>
    /// Deletes logs older than <see cref="RetainedDays"/>. Screen-related diagnostics can
    /// include window titles, so they should not accumulate indefinitely.
    /// </summary>
    private static void PruneOldLogs()
    {
        try
        {
            DateTime cutoff = DateTime.Now.AddDays(-RetainedDays);
            foreach (string file in Directory.EnumerateFiles(LogDirectory, "cursorai-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not prune old logs: {exception.Message}");
        }
    }

    /// <summary>
    /// Writes to the console and the log at once, timestamping only the log so terminal output
    /// stays as readable as it was.
    /// </summary>
    private sealed class TeeTextWriter(TextWriter console, StreamWriter log, string tag) : TextWriter
    {
        private readonly StringBuilder _pending = new();
        private readonly object _gate = new();

        public override Encoding Encoding => console.Encoding;

        public override void Write(char value)
        {
            console.Write(value);

            lock (_gate)
            {
                if (value == '\n')
                {
                    Flush(_pending.ToString());
                    _pending.Clear();
                }
                else if (value != '\r')
                {
                    _pending.Append(value);
                }
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
            {
                return;
            }

            foreach (char c in value)
            {
                Write(c);
            }
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        private void Flush(string line)
        {
            try
            {
                log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {tag} {line}");
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // A full or removed disk must not break console output.
            }
        }
    }
}
