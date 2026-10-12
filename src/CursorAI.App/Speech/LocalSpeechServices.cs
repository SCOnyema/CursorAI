using System.Diagnostics;

namespace CursorAI.App.Speech;

/// <summary>
/// Creates the local speech engines, treating absence as a degraded mode rather than a failure.
/// </summary>
/// <remarks>
/// Speech depends on machine state CursorAI does not control: an installed recognition language,
/// a working microphone, an output device. Any of those can be missing. The hotkey conflict that
/// killed this application at startup was the same shape of mistake, so neither engine is
/// allowed to prevent CursorAI from running: vision by hotkey keeps working without voice.
/// </remarks>
public sealed class LocalSpeechServices : IDisposable
{
    private bool _isDisposed;

    private LocalSpeechServices(
        ISpeechRecognizer? recognizer,
        ISpeechSynthesizer? synthesizer,
        IReadOnlyList<string> unavailable)
    {
        Recognizer = recognizer;
        Synthesizer = synthesizer;
        Unavailable = unavailable;
    }

    /// <summary>Null when speech recognition is unavailable on this machine.</summary>
    public ISpeechRecognizer? Recognizer { get; }

    /// <summary>Null when speech synthesis is unavailable on this machine.</summary>
    public ISpeechSynthesizer? Synthesizer { get; }

    /// <summary>Reasons any engine is unavailable, for reporting at startup.</summary>
    public IReadOnlyList<string> Unavailable { get; }

    public bool CanListen => Recognizer is not null;

    public bool CanSpeak => Synthesizer is not null;

    public static LocalSpeechServices Create()
    {
        List<string> unavailable = [];
        ISpeechRecognizer? recognizer = TryCreate(
            static () => new WindowsSpeechRecognizer(), "Speech recognition", unavailable);
        ISpeechSynthesizer? synthesizer = TryCreate(
            static () => new WindowsSpeechSynthesizer(), "Speech synthesis", unavailable);

        return new LocalSpeechServices(recognizer, synthesizer, unavailable);
    }

    private static T? TryCreate<T>(Func<T> create, string role, List<string> unavailable)
        where T : class
    {
        try
        {
            return create();
        }
        catch (Exception exception)
        {
            // Deliberately broad: System.Speech surfaces missing engines, absent audio devices
            // and COM registration problems as several unrelated exception types, and none of
            // them is a reason to stop the application.
            unavailable.Add($"{role}: {exception.Message}");
            Debug.WriteLine($"{role} unavailable: {exception}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Recognizer?.Dispose();
        Synthesizer?.Dispose();
    }
}
