namespace CursorAI.App.Speech;

/// <summary>
/// Captures microphone audio as 16 kHz mono 16-bit PCM, which is what Whisper expects.
/// </summary>
/// <remarks>
/// An interface so the recognizer can be tested without a microphone or a model file.
/// </remarks>
internal interface IAudioRecorder : IDisposable
{
    /// <summary>True while audio is being captured.</summary>
    bool IsRecording { get; }

    /// <summary>Begins capture. Throws when no input device is available.</summary>
    void Start();

    /// <summary>
    /// Stops capture and returns everything recorded as 16 kHz mono 16-bit PCM samples,
    /// without a WAV header. Returns an empty span when nothing was captured.
    /// </summary>
    byte[] StopAndRead();
}
