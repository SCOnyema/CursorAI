using System.Windows;
using CursorAI.App.State;
using CursorAI.App.Vision;

namespace CursorAI.App;

public partial class App : Application
{
    private readonly GeminiVisionProvider _visionProvider;

    public App()
    {
        DevelopmentConsole.TryAttachToParent();
        _visionProvider = new GeminiVisionProvider();
        VisionService = new VisionService(_visionProvider);
    }

    public BuddyStateManager BuddyStateManager { get; } = new();

    public VisionService VisionService { get; }

    protected override void OnExit(ExitEventArgs e)
    {
        _visionProvider.Dispose();
        base.OnExit(e);
    }
}
