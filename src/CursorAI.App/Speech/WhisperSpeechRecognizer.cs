using System.Diagnostics;
using System.IO;
using System.Text;
using Whisper.net;

namespace CursorAI.App.Speech;

/// <summary>
/// On-device speech recognition using Whisper, via the whisper.cpp binding.
/// </summary>
/// <remarks>
/// Replaces the legacy Windows recognizer, which misheard badly: "what about Michael and so"
/// for a question about Kotter's change model, and "But what you seem to" for another.
///
/// Whisper does not stream, so the flow is push-to-talk: record while the key is held, then
/// transcribe the whole utterance on release. That also removes the legacy engine's voice
/// activity detection, which was truncating utterances mid-sentence.
///
/// Entirely on-device. Whisper.net and Whisper.net.Runtime are both MIT (verified from the
/// package metadata, not from documentation), the model file is local, and no audio leaves the
/// machine - which is the same privacy property the legacy recognizer had and the reason cloud
/// transcription was rejected.
/// </remarks>
internal sealed class WhisperSpeechRecognizer : ISpeechRecognizer
{
    private readonly string _modelPath;
    private readonly IAudioRecorder _recorder;
    private readonly bool _ownsRecorder;
    private WhisperFactory? _factory;
    private bool _isDisposed;

    /// <summary>
    /// Verifies the model is loadable up front, so an unusable model fails at startup where it
    /// can fall back to the legacy recognizer, rather than on the first hotkey press.
    /// </summary>
    internal WhisperSpeechRecognizer(string modelPath)
        : this(modelPath, new MicrophoneRecorder(), ownsRecorder: true)
    {
    }

    internal WhisperSpeechRecognizer(string modelPath, IAudioRecorder recorder, bool ownsRecorder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(recorder);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                WhisperModelLocator.DescribeMissingModel(modelPath), modelPath);
        }

        _modelPath = modelPath;
        _recorder = recorder;
        _ownsRecorder = ownsRecorder;
        Description = $"Whisper ({Path.GetFileName(modelPath)}, on-device)";
    }

    public string Description { get; }

    public async Task<string?> RecognizeOnceAsync(
        TimeSpan timeout,
        Func<bool>? keepRecording,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] pcm = await RecordAsync(timeout, keepRecording, cancellationToken);
        if (pcm.Length == 0)
        {
            return null;
        }

        return await TranscribeAsync(pcm, cancellationToken);
    }

    /// <summary>
    /// Records while <paramref name="keepRecording"/> holds. With no predicate it records for
    /// the whole timeout, since Whisper has no notion of an utterance ending.
    /// </summary>
    private async Task<byte[]> RecordAsync(
        TimeSpan timeout,
        Func<bool>? keepRecording,
        CancellationToken cancellationToken)
    {
        _recorder.Start();
        try
        {
            // A floor on duration: releasing the key almost immediately would otherwise
            // capture too little audio for Whisper to do anything with.
            TimeSpan minimum = TimeSpan.FromMilliseconds(350);
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < timeout)
            {
                await Task.Delay(30, cancellationToken);

                if (keepRecording is null)
                {
                    continue;
                }

                if (!keepRecording() && elapsed.Elapsed >= minimum)
                {
                    break;
                }
            }
        }
        finally
        {
            // Stop capture even when cancelled, so the device is never left open.
        }

        return _recorder.StopAndRead();
    }

    private async Task<string?> TranscribeAsync(byte[] pcm, CancellationToken cancellationToken)
    {
        _factory ??= WhisperFactory.FromPath(_modelPath);

        using WhisperProcessor processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .Build();

        using MemoryStream wave = new();
        WriteWaveHeader(wave, pcm.Length);
        wave.Write(pcm, 0, pcm.Length);
        wave.Position = 0;

        StringBuilder transcript = new();
        await foreach (SegmentData segment in processor.ProcessAsync(wave, cancellationToken))
        {
            transcript.Append(segment.Text);
        }

        string text = transcript.ToString().Trim();

        // Whisper emits bracketed annotations such as [BLANK_AUDIO] or (silence) for audio with
        // no speech in it. Those are not a question the user asked.
        if (text.Length == 0 || IsOnlyAnnotation(text))
        {
            return null;
        }

        return text;
    }

    private static bool IsOnlyAnnotation(string text)
    {
        string trimmed = text.Trim();
        return (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            || (trimmed.StartsWith('(') && trimmed.EndsWith(')') && !trimmed.Contains(' '));
    }

    /// <summary>
    /// Writes a 44-byte canonical WAV header for 16 kHz mono 16-bit PCM, because the processor
    /// reads a stream with a header rather than raw samples.
    /// </summary>
    private static void WriteWaveHeader(Stream stream, int dataLength)
    {
        const int sampleRate = MicrophoneRecorder.SampleRate;
        const short channels = MicrophoneRecorder.Channels;
        const short bitsPerSample = MicrophoneRecorder.BitsPerSample;
        const short blockAlign = channels * (bitsPerSample / 8);
        const int byteRate = sampleRate * blockAlign;

        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _factory?.Dispose();

        if (_ownsRecorder)
        {
            _recorder.Dispose();
        }
    }
}
