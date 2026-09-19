namespace CursorAI.App.Capture;

public sealed class ForegroundWindowInfo
{
    internal ForegroundWindowInfo(nint windowHandle, string? title, string? processName)
    {
        WindowHandle = windowHandle;
        Title = title;
        ProcessName = processName;
    }

    internal nint WindowHandle { get; }

    public string? Title { get; }

    public string? ProcessName { get; }
}
