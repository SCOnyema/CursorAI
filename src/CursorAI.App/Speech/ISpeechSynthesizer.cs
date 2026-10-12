namespace CursorAI.App.Speech;

/// <summary>
/// Speaks text aloud. Local-only, for the same reasons as <see cref="ISpeechRecognizer"/>.
/// </summary>
public interface ISpeechSynthesizer : IDisposable
{
    string Description { get; }

    /// <summary>Speaks <paramref name="text"/>, completing when playback finishes.</summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken);

    /// <summary>Stops playback immediately. Safe to call when nothing is speaking.</summary>
    void Cancel();
}
