using System.Windows;

namespace CursorAI.App;

internal static class BuddyPositioning
{
    internal static Point Calculate(
        Point cursorPosition,
        Rect workArea,
        Size buddySize,
        Vector cursorOffset)
    {
        double maximumLeft = Math.Max(workArea.Left, workArea.Right - buddySize.Width);
        double maximumTop = Math.Max(workArea.Top, workArea.Bottom - buddySize.Height);

        return new Point(
            Math.Clamp(cursorPosition.X + cursorOffset.X, workArea.Left, maximumLeft),
            Math.Clamp(cursorPosition.Y + cursorOffset.Y, workArea.Top, maximumTop));
    }
}
