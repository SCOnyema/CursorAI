using System.Windows;
using CursorAI.App.Speech;
using CursorAI.App.State;
using CursorAI.App.Vision;

namespace CursorAI.App;

public partial class App : Application
{
    private readonly IVisionProvider _visionProvider;

    public App()
    {
        DevelopmentConsole.TryAttachToParent();
        try
        {
            _visionProvider = VisionProviderFactory.Create();
        }
        catch (InvalidOperationException exception)
        {
            // A misconfigured provider means we do not know which vendor would receive
            // screenshots, so this must fail rather than fall back. Print the reason plainly
            // first: the raw unhandled exception is unreadable, and invisible from Explorer.
            Console.Error.WriteLine("CursorAI cannot start.");
            Console.Error.WriteLine(exception.Message);
            throw;
        }

        VisionService = new VisionService(_visionProvider);
        Console.WriteLine($"Vision provider: {_visionProvider.Description}");

        SpeechServices = LocalSpeechServices.Create();
        ReportSpeechAvailability();
    }

    public BuddyStateManager BuddyStateManager { get; } = new();

    public VisionService VisionService { get; }

    public LocalSpeechServices SpeechServices { get; }

    /// <summary>
    /// Says plainly what voice can and cannot do on this machine. Silence here would leave a
    /// missing microphone looking like a broken feature.
    /// </summary>
    private void ReportSpeechAvailability()
    {
        if (SpeechServices.Recognizer is { } recognizer)
        {
            Console.WriteLine($"Speech input: {recognizer.Description}");
        }

        if (SpeechServices.Synthesizer is { } synthesizer)
        {
            Console.WriteLine($"Speech output: {synthesizer.Description}");
        }

        foreach (string reason in SpeechServices.Unavailable)
        {
            Console.Error.WriteLine($"Voice unavailable - {reason}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SpeechServices.Dispose();
        _visionProvider.Dispose();
        base.OnExit(e);
    }
}
