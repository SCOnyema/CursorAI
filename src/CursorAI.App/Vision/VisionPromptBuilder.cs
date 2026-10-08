namespace CursorAI.App.Vision;

internal static class VisionPromptBuilder
{
    internal static string Build(VisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string processName = request.ForegroundWindow.ProcessName ?? "<unavailable>";
        string windowTitle = request.ForegroundWindow.Title ?? "<unavailable>";

        return $"""
            You are analyzing the user's current Windows screen.

            Screenshot size: {request.ImagePixelWidth}x{request.ImagePixelHeight} pixels.
            Captured at: {request.CapturedAt:O}.
            Monitor bounds in physical screen pixels: X={request.Monitor.Bounds.X}, Y={request.Monitor.Bounds.Y}, Width={request.Monitor.Bounds.Width}, Height={request.Monitor.Bounds.Height}.
            Monitor work area in physical screen pixels: X={request.Monitor.WorkArea.X}, Y={request.Monitor.WorkArea.Y}, Width={request.Monitor.WorkArea.Width}, Height={request.Monitor.WorkArea.Height}.
            Primary monitor: {request.Monitor.IsPrimary}.
            The coordinate origin is the top-left of the screenshot.
            The cursor's physical screen position is ({request.CursorScreenPosition.X}, {request.CursorScreenPosition.Y}).
            The user's cursor is at pixel ({request.CursorPositionInImage.X}, {request.CursorPositionInImage.Y}) relative to the screenshot.
            The cursor may not be visibly rendered in the screenshot; use the supplied coordinates to reason about where the user is pointing.
            Foreground application: {processName}.
            Foreground window title: {windowTitle}.

            User question:
            {request.UserPrompt}
            """;
    }
}
