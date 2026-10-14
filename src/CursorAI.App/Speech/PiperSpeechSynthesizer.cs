using System.Diagnostics;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace CursorAI.App.Speech;

/// <summary>
/// Local neural speech synthesis through Piper, kept alive as a persistent process.
/// </summary>
/// <remarks>
/// Persistence is the whole design, not an optimisation. Measured on the development machine:
/// spawning Piper per answer took 6.74s wall time for 9.8s of speech, of which only 1.55s was
/// inference - the rest was process start and ONNX model load. Held open, time to first audio
/// was 0.65s on the first utterance and 0.45-0.49s warm.
///
/// Raw PCM is streamed from stdout and played as it arrives, so playback begins long before
/// synthesis finishes. Piper emits no delimiter between utterances, so the end of one is
/// inferred from the stream going idle - see <see cref="IdleGap"/>. That heuristic only decides
/// when SpeakAsync returns; it cannot cut speech short, because playback drains whatever has
/// already been buffered.
///
/// Entirely on-device: no audio and no text leaves the machine.
/// </remarks>
internal sealed class PiperSpeechSynthesizer : ISpeechSynthesizer
{
    /// <summary>
    /// How long the output stream must stay quiet before an utterance is considered finished.
    /// Comfortably longer than the observed inter-chunk gap, and short enough not to feel like
    /// a pause at the end of an answer.
    /// </summary>
    private static readonly TimeSpan IdleGap = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Longest to wait for the first audio of an utterance before giving up. Needed because the
    /// idle rule below cannot distinguish "finished" from "has not started": applying it before
    /// any audio arrives made SpeakAsync return in 0.41s for ten seconds of speech.
    /// </summary>
    private static readonly TimeSpan FirstAudioTimeout = TimeSpan.FromSeconds(20);

    private readonly string _executablePath;
    private readonly string _voicePath;
    private readonly int _sampleRate;
    private readonly object _gate = new();

    private Process? _piper;
    private BufferedWaveProvider? _bufferedAudio;
    private WaveOut? _output;
    private DateTime _lastChunkUtc;
    private long _chunksThisUtterance;
    private bool _isDisposed;

    internal PiperSpeechSynthesizer(string executablePath, string voicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(voicePath);

        if (!File.Exists(executablePath) || !File.Exists(voicePath))
        {
            throw new FileNotFoundException(
                PiperVoiceLocator.DescribeMissing(executablePath, voicePath));
        }

        _executablePath = executablePath;
        _voicePath = voicePath;
        _sampleRate = PiperVoiceLocator.ReadSampleRate(voicePath);
        Description = $"Piper ({Path.GetFileNameWithoutExtension(voicePath)}, on-device, {_sampleRate} Hz)";
    }

    public string Description { get; }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        EnsureStarted();

        Process piper = _piper
            ?? throw new InvalidOperationException("Piper is not running.");
        BufferedWaveProvider buffered = _bufferedAudio!;

        // Newlines delimit utterances on stdin, so an answer containing them would be read as
        // several separate requests.
        string single = text.Replace('\r', ' ').Replace('\n', ' ').Trim();

        lock (_gate)
        {
            buffered.ClearBuffer();
            _lastChunkUtc = DateTime.UtcNow;
            _chunksThisUtterance = 0;
        }

        _output!.Play();
        await piper.StandardInput.WriteLineAsync(single.AsMemory(), cancellationToken);
        await piper.StandardInput.FlushAsync(cancellationToken);

        try
        {
            await WaitForSynthesisAsync(cancellationToken);
            await WaitForPlaybackAsync(buffered, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Cancel();
            throw;
        }
    }

    /// <summary>
    /// Waits for the utterance to finish being synthesised: first for audio to start arriving,
    /// then for the stream to go quiet for <see cref="IdleGap"/>. The two phases are distinct
    /// because an idle stream means "not started yet" until the first chunk appears.
    /// </summary>
    private async Task WaitForSynthesisAsync(CancellationToken cancellationToken)
    {
        Stopwatch waiting = Stopwatch.StartNew();

        while (true)
        {
            await Task.Delay(50, cancellationToken);

            lock (_gate)
            {
                if (_chunksThisUtterance == 0)
                {
                    if (waiting.Elapsed >= FirstAudioTimeout)
                    {
                        // Nothing ever arrived: a crashed or wedged process. Returning lets the
                        // caller carry on rather than hanging the voice turn forever.
                        return;
                    }

                    continue;
                }

                if (DateTime.UtcNow - _lastChunkUtc >= IdleGap)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Waits for buffered audio to finish playing, so SpeakAsync means "spoken".</summary>
    private static async Task WaitForPlaybackAsync(
        BufferedWaveProvider buffered,
        CancellationToken cancellationToken)
    {
        while (buffered.BufferedBytes > 0)
        {
            await Task.Delay(50, cancellationToken);
        }
    }

    private void EnsureStarted()
    {
        if (_piper is { HasExited: false })
        {
            return;
        }

        StopProcess();

        ProcessStartInfo startInfo = new(_executablePath)
        {
            // --output_raw streams headerless PCM to stdout, which is what makes playback able
            // to start before synthesis completes.
            Arguments = $"-m \"{_voicePath}\" --output_raw",
            WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? string.Empty,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        Process piper = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Piper could not be started.");

        _piper = piper;
        // Sixty seconds of headroom, so a long answer is never dropped. NAudio 3 takes the
        // duration on the constructor; BufferLength and BufferDuration are both read-only.
        WaveFormat format = new(_sampleRate, 16, 1);
        _bufferedAudio = new BufferedWaveProvider(format, TimeSpan.FromSeconds(60))
        {
            DiscardOnBufferOverflow = true
        };

        _output = new WaveOut();
        _output.Init(_bufferedAudio);

        _ = Task.Run(() => PumpAudioAsync(piper, _bufferedAudio));
        _ = Task.Run(() => DrainErrorsAsync(piper));
        _lastChunkUtc = DateTime.UtcNow;
    }

    /// <summary>Copies stdout into the playback buffer, recording when audio last arrived.</summary>
    private async Task PumpAudioAsync(Process piper, BufferedWaveProvider buffered)
    {
        byte[] chunk = new byte[8192];
        Stream stdout = piper.StandardOutput.BaseStream;

        try
        {
            int read;
            while ((read = await stdout.ReadAsync(chunk)) > 0)
            {
                lock (_gate)
                {
                    _lastChunkUtc = DateTime.UtcNow;
                    _chunksThisUtterance++;
                }

                buffered.AddSamples(chunk, 0, read);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Normal on shutdown or restart.
        }
    }

    /// <summary>
    /// Piper logs progress to stderr. Left unread it would eventually fill the pipe and block
    /// synthesis, so it is drained and discarded.
    /// </summary>
    private static async Task DrainErrorsAsync(Process piper)
    {
        try
        {
            while (await piper.StandardError.ReadLineAsync() is { } line)
            {
                Debug.WriteLine($"piper: {line}");
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    public void Cancel()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            _output?.Stop();
            _bufferedAudio?.ClearBuffer();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Stopping Piper playback failed: {exception.GetType().Name}");
        }

        // The process may still be mid-utterance with audio queued behind it, and there is no
        // way to tell it to stop. Restarting is the only reliable way to discard that, and it
        // costs the one-off model load again.
        StopProcess();
    }

    private void StopProcess()
    {
        Process? piper = _piper;
        _piper = null;

        if (piper is not null)
        {
            try
            {
                if (!piper.HasExited)
                {
                    piper.StandardInput.Close();
                    if (!piper.WaitForExit((int)StartupTimeout.TotalMilliseconds / 10))
                    {
                        piper.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Stopping Piper failed: {exception.GetType().Name}");
            }
            finally
            {
                piper.Dispose();
            }
        }

        _output?.Dispose();
        _output = null;
        _bufferedAudio = null;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopProcess();
    }
}
