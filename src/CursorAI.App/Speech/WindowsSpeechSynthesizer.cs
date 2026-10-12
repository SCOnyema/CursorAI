using System.Speech.Synthesis;

namespace CursorAI.App.Speech;

/// <summary>
/// Speaks through the Windows speech synthesiser. Entirely on-device: no audio leaves the
/// machine and no provider quota is consumed.
/// </summary>
public sealed class WindowsSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly SpeechSynthesizer _synthesizer;
    private bool _isDisposed;

    public WindowsSpeechSynthesizer()
    {
        _synthesizer = new SpeechSynthesizer();
        _synthesizer.SetOutputToDefaultAudioDevice();
        Description = $"Windows speech synthesis ({_synthesizer.Voice.Name})";
    }

    public string Description { get; }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Prompt prompt = new(text);

        void OnCompleted(object? sender, SpeakCompletedEventArgs e)
        {
            if (!ReferenceEquals(e.Prompt, prompt))
            {
                return;
            }

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
                completion.TrySetResult();
            }
        }

        _synthesizer.SpeakCompleted += OnCompleted;

        // Cancelling stops playback, which surfaces as SpeakCompleted with Cancelled set.
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((SpeechSynthesizer)state!).SpeakAsyncCancelAll(),
            _synthesizer);

        try
        {
            _synthesizer.SpeakAsync(prompt);
            await completion.Task;
        }
        finally
        {
            _synthesizer.SpeakCompleted -= OnCompleted;
        }
    }

    public void Cancel()
    {
        if (!_isDisposed)
        {
            _synthesizer.SpeakAsyncCancelAll();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _synthesizer.Dispose();
    }
}
