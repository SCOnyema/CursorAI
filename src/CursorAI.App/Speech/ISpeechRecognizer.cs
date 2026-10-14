namespace CursorAI.App.Speech;

/// <summary>
/// Converts one spoken utterance into text. Implementations run entirely on the local machine:
/// microphone audio never crosses the external trust boundary, so voice adds no new network
/// surface and consumes none of the vision provider's request quota.
/// </summary>
public interface ISpeechRecognizer : IDisposable
{
    /// <summary>Human-readable engine description, for diagnostics.</summary>
    string Description { get; }

    /// <summary>
    /// Listens for a single utterance and returns what was heard, or null when nothing
    /// intelligible was said before <paramref name="timeout"/> elapsed.
    /// </summary>
    /// <param name="keepRecording">
    /// Push-to-talk signal, polled while capturing: recording continues while it returns true.
    /// Whisper cannot stream, so it needs an explicit end to the utterance rather than voice
    /// activity detection. Engines with their own end-of-speech detection ignore this, and a
    /// null predicate means "let the engine decide", which for Whisper means the full timeout.
    /// </param>
    Task<string?> RecognizeOnceAsync(
        TimeSpan timeout,
        Func<bool>? keepRecording,
        CancellationToken cancellationToken);
}
