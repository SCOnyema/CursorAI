namespace CursorAI.App.Vision;

internal static class VisionPromptBuilder
{
    internal static string Build(VisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string processName = request.ForegroundWindow.ProcessName ?? "<unavailable>";
        string windowTitle = request.ForegroundWindow.Title ?? "<unavailable>";

        string prompt = $"""
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

        return request.RequestsTargets ? prompt + TargetInstructions : prompt;
    }

    /// <summary>
    /// Asks for located elements in Gemini's native normalised convention.
    /// </summary>
    /// <remarks>
    /// Measured against synthetic screens with known rectangles on 2026-10-12. Requesting
    /// boxes in raw screenshot pixels produced a systematic vertical scaling error - mean IoU
    /// 0.24, centres 29 to 54 pixels out, enough for a highlight to visibly miss its target.
    /// Requesting the normalised 0-1000 box_2d convention instead gave mean IoU 0.91 with
    /// centres within 2 pixels. The model is calibrated on that convention, so ask for it and
    /// let the provider convert.
    /// </remarks>
    private const string TargetInstructions = """


            Reply with ONLY a JSON object, no prose outside it and no code fence:
            {"answer":"<your answer in one or two sentences>","targets":[{"label":"<visible text>","box_2d":[ymin,xmin,ymax,xmax]}]}
            Every box_2d is normalised to 0-1000 over the image height and width, in that order.

            A target must be something the user could act on: a button, menu item, tab, link,
            checkbox, text field or similar control. Box the control itself, not a caption
            beside it. Do not return headings, paragraphs, labels or other passages of text,
            and never return a target larger than roughly a quarter of the screen. If the
            answer refers to no actionable control, return an empty targets array.
            """;
}
