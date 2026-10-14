using System.Windows;
using CursorAI.App.Diagnostics;
using CursorAI.App.Input;
using CursorAI.App.Speech;
using CursorAI.App.State;
using CursorAI.App.Vision;

namespace CursorAI.App;

public partial class App : Application
{
    /// <summary>
    /// Session-scoped, so one signed-in user cannot start two CursorAI processes. Two instances
    /// fight over the same global hotkeys, and the loser reports them as owned by "another
    /// application" - true, but deeply confusing when that application is itself.
    /// </summary>
    private const string SingleInstanceMutexName = @"Local\CursorAI.App.SingleInstance";

    private const int DuplicateInstanceExitCode = 2;

    private readonly Mutex _singleInstanceMutex;
    private readonly IVisionProvider _visionProvider;

    public App()
    {
        DevelopmentConsole.TryAttachToParent();

        // Started before anything else can fail, so a startup failure is still recorded.
        FileLog.Start();

        // Checked before anything is constructed, so a duplicate launch never grabs the
        // microphone or a provider transport on its way out.
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out bool isOnlyInstance);
        if (!isOnlyInstance)
        {
            Console.Error.WriteLine(
                "CursorAI is already running. Use the quit shortcut on the running instance "
                + $"(default {HotkeyConfiguration.DefaultQuit}) instead of starting a second one.");
            Environment.Exit(DuplicateInstanceExitCode);
        }

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
    /// Short provider name for the panel, so the user can see where their screen is going.
    /// </summary>
    public string ProviderDisplayName =>
        _visionProvider.Description.Split('(')[0].Trim() is { Length: > 0 } name
            ? name
            : "the vision provider";

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
        FileLog.Stop();
        SpeechServices.Dispose();
        _visionProvider.Dispose();
        _singleInstanceMutex.Dispose();
        base.OnExit(e);
    }
}
