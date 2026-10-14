using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace CursorAI.App.Speech;

/// <summary>
/// Captures the default microphone at 16 kHz mono 16-bit PCM.
/// </summary>
/// <remarks>
/// That format is chosen because it is exactly what Whisper consumes, so no resampling is
/// needed and the recorded buffer can go straight to the model. Capture is driven by NAudio's
/// WaveIn rather than hand-rolled winmm interop: buffer-callback bugs in audio capture
/// are unpleasant to diagnose, and this is not where the project's P/Invoke effort is best
/// spent.
///
/// A hard cap exists so a stuck push-to-talk key cannot grow the buffer without bound.
/// </remarks>
internal sealed class MicrophoneRecorder : IAudioRecorder
{
    internal const int SampleRate = 16000;
    internal const int Channels = 1;
    internal const int BitsPerSample = 16;

    /// <summary>
    /// Longest utterance kept. At 16 kHz mono 16-bit that is 32 KB per second, so this bounds
    /// the buffer at about 3.8 MB.
    /// </summary>
    internal static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(120);

    private readonly object _gate = new();
    private readonly MemoryStream _buffer = new();
    private readonly int _maximumBytes =
        (int)(SampleRate * Channels * (BitsPerSample / 8) * MaximumDuration.TotalSeconds);

    private WaveIn? _device;
    private bool _isDisposed;

    public bool IsRecording => _device is not null;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_device is not null)
        {
            return;
        }

        lock (_gate)
        {
            _buffer.SetLength(0);
        }

        WaveIn device = new()
        {
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            // Short buffers so releasing the key stops capture promptly rather than after a
            // noticeable tail.
            BufferMilliseconds = 50
        };
        device.DataAvailable += OnDataAvailable;
        device.RecordingStopped += OnRecordingStopped;

        try
        {
            device.StartRecording();
        }
        catch
        {
            device.DataAvailable -= OnDataAvailable;
            device.RecordingStopped -= OnRecordingStopped;
            device.Dispose();
            throw;
        }

        _device = device;
    }

    public byte[] StopAndRead()
    {
        WaveIn? device = _device;
        _device = null;

        if (device is not null)
        {
            try
            {
                device.StopRecording();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Stopping capture failed: {exception.GetType().Name}: {exception.Message}");
            }

            device.DataAvailable -= OnDataAvailable;
            device.RecordingStopped -= OnRecordingStopped;
            device.Dispose();
        }

        lock (_gate)
        {
            return _buffer.ToArray();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            int remaining = _maximumBytes - (int)_buffer.Length;
            if (remaining <= 0)
            {
                return;
            }

            _buffer.Write(e.Buffer, 0, Math.Min(e.BytesRecorded, remaining));
        }
    }

    private static void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Debug.WriteLine($"Capture stopped with error: {e.Exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _ = StopAndRead();
        _buffer.Dispose();
    }
}
