using System.Globalization;
using System.Speech.Recognition;

namespace CursorAI.App.Speech;

/// <summary>
/// Recognises one spoken utterance using the Windows speech recogniser. Entirely on-device:
/// microphone audio never crosses the external trust boundary.
/// </summary>
public sealed class WindowsSpeechRecognizer : ISpeechRecognizer
{
    private readonly SpeechRecognitionEngine _engine;
    private bool _isDisposed;

    public WindowsSpeechRecognizer()
    {
        RecognizerInfo recognizer = SelectRecognizer();
        _engine = new SpeechRecognitionEngine(recognizer);
        _engine.LoadGrammar(new DictationGrammar());
        _engine.SetInputToDefaultAudioDevice();
        Description = $"Windows speech recognition ({recognizer.Name}, {recognizer.Culture.Name})";
    }

    public string Description { get; }

    /// <summary>
    /// The legacy engine has its own voice activity detection, so the push-to-talk signal is
    /// not applicable and is ignored.
    /// </summary>
    public async Task<string?> RecognizeOnceAsync(
        TimeSpan timeout,
        Func<bool>? keepRecording,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Bound how long the engine waits for speech to begin, so a stray activation does not
        // leave the microphone open indefinitely.
        _engine.InitialSilenceTimeout = timeout;
        _engine.BabbleTimeout = timeout;
        _engine.EndSilenceTimeout = TimeSpan.FromSeconds(1);

        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(object? sender, RecognizeCompletedEventArgs e)
        {
            if (e.Error is not null)
            {
                completion.TrySetException(e.Error);
            }
            else if (e.Cancelled)
            {
                completion.TrySetCanceled();
            }
            else
            {
                // Null covers both silence and audio the engine could not interpret.
                completion.TrySetResult(e.Result?.Text);
            }
        }

        _engine.RecognizeCompleted += OnCompleted;

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((SpeechRecognitionEngine)state!).RecognizeAsyncCancel(),
            _engine);

        try
        {
            _engine.RecognizeAsync(RecognizeMode.Single);
            string? heard = await completion.Task;
            return string.IsNullOrWhiteSpace(heard) ? null : heard.Trim();
        }
        finally
        {
            _engine.RecognizeCompleted -= OnCompleted;
        }
    }

    /// <summary>
    /// Prefers a recogniser matching the current UI culture, then any installed one. Hardcoding
    /// en-US would throw outright on a machine with only another language installed.
    /// </summary>
    private static RecognizerInfo SelectRecognizer()
    {
        List<RecognizerInfo> installed = [.. SpeechRecognitionEngine.InstalledRecognizers()];
        if (installed.Count == 0)
        {
            throw new InvalidOperationException(
                "No Windows speech recognition engine is installed, so voice input is "
                + "unavailable. Add a speech language in Windows Settings to enable it.");
        }

        CultureInfo current = CultureInfo.CurrentUICulture;
        return installed.Find(r => r.Culture.Equals(current))
            ?? installed.Find(r => r.Culture.TwoLetterISOLanguageName == current.TwoLetterISOLanguageName)
            ?? installed[0];
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _engine.Dispose();
    }
}
