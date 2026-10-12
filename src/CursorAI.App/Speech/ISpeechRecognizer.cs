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
    Task<string?> RecognizeOnceAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
