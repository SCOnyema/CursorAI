using System.Text;

namespace CursorAI.App.Vision;

internal static class VisionPromptBuilder
{
    /// <summary>
    /// Who CursorAI is and how it answers.
    /// </summary>
    /// <remarks>
    /// Before this existed the prompt was screen geometry followed by the question, with
    /// nothing about purpose or manner. Handed six lines of cursor and monitor coordinates and
    /// no other guidance, the model reasonably concluded the cursor was the point, and answers
    /// opened by narrating the screen whatever was asked.
    /// </remarks>
    private const string Instructions = """
        You are CursorAI, a friendly assistant that sits beside the user's mouse cursor on
        Windows. The user asks about whatever is on their screen, and you help them
        understand it and get things done.

        How to answer:
        - Answer the question the user actually asked, directly. Do not open by describing
          the screen or saying where the cursor is.
        - Mention the screen, the cursor or the application only when that helps the answer.
          When the user says "this", "here" or "that", they usually mean what is under the
          cursor.
        - When the user asks how to do something, give short numbered steps naming the exact
          buttons, menus or fields they will see.
        - Keep simple answers to a sentence or two. Be warm and plain-spoken.
        - Say so when something is not visible or you are unsure, rather than guessing.
        """;

    private const string HistoryInstructions = """
        This question continues an earlier conversation. Use it to work out what "it", "that"
        or "the next step" refers to. The screen may have changed since; trust the current
        screenshot over what was said earlier.
        """;

    internal static string Build(VisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string processName = request.ForegroundWindow.ProcessName ?? "<unavailable>";
        string windowTitle = request.ForegroundWindow.Title ?? "<unavailable>";

        StringBuilder prompt = new();
        prompt.Append(Instructions).Append("\n\n");

        if (request.History.Count > 0)
        {
            prompt.Append(HistoryInstructions).Append('\n');
            prompt.Append("Earlier in this conversation, oldest first:\n");
            foreach (ConversationTurn turn in request.History)
            {
                prompt.Append("User: ").Append(turn.Question).Append('\n');
                prompt.Append("CursorAI: ").Append(turn.Answer).Append("\n\n");
            }
        }

        // Monitor bounds are deliberately absent: located elements are reported in a
        // normalised convention over the screenshot, so the model never needs desktop
        // geometry, and including it only invited answers about it.
        prompt.Append($"""
            Current screen:
            Foreground application: {processName}.
            Foreground window title: {windowTitle}.
            Screenshot size: {request.ImagePixelWidth}x{request.ImagePixelHeight} pixels, origin at the top-left.
            The user's cursor is at pixel ({request.CursorPositionInImage.X}, {request.CursorPositionInImage.Y}) in the screenshot. It may not be drawn there, so use these coordinates when the user points at something.
            Captured at: {request.CapturedAt:O}.

            User question:
            {request.UserPrompt}
            """);

        if (request.RequestsTargets)
        {
            prompt.Append(TargetInstructions);
        }

        return prompt.ToString();
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
            {"answer":"<your answer, following the guidance above>","targets":[{"label":"<visible text>","box_2d":[ymin,xmin,ymax,xmax]}]}
            Every box_2d is normalised to 0-1000 over the image height and width, in that order.

            A target must be something the user could act on: a button, menu item, tab, link,
            checkbox, text field or similar control. Box the control itself, not a caption
            beside it. Do not return headings, paragraphs, labels or other passages of text,
            and never return a target larger than roughly a quarter of the screen. If the
            answer refers to no actionable control, return an empty targets array.
            """;
}
