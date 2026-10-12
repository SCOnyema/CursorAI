using System.Windows;
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
    }

    public BuddyStateManager BuddyStateManager { get; } = new();

    public VisionService VisionService { get; }

    protected override void OnExit(ExitEventArgs e)
    {
        _visionProvider.Dispose();
        base.OnExit(e);
    }
}
