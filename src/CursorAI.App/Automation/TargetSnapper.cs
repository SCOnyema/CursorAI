using CursorAI.App.Capture;
using CursorAI.App.Overlay;
using CursorAI.App.Vision;

namespace CursorAI.App.Automation;

/// <summary>
/// Refines model-estimated targets using real UI Automation element bounds.
/// </summary>
/// <remarks>
/// The vision model understands the screen; UI Automation knows exactly where things are. This
/// combines them: each model box is matched to the element that best overlaps it, and that
/// element's bounds replace the estimate. Where UI Automation reports nothing usable - Electron
/// and Chromium windows, games, custom-drawn interfaces - the model's own coordinates are kept
/// unchanged, so nothing regresses on the applications it cannot see into.
/// </remarks>
internal static class TargetSnapper
{
    /// <summary>
    /// Minimum overlap before an element is considered the same thing as a model box. Low
    /// enough to match a button when the model boxed only its label text, high enough that a
    /// nameless pane spanning the window never qualifies: such a pane scores well under 0.01
    /// against a button-sized box.
    /// </summary>
    internal const double MinimumOverlap = 0.25;

    /// <summary>
    /// Returns targets with bounds replaced by matching elements where possible. Input and
    /// output are both in screenshot pixels; elements are in physical screen pixels.
    /// </summary>
    internal static IReadOnlyList<VisionTarget> Snap(
        IReadOnlyList<VisionTarget> targets,
        IReadOnlyList<UiElement> elements,
        PhysicalScreenBounds monitorBounds)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(elements);

        if (elements.Count == 0 || targets.Count == 0)
        {
            return targets;
        }

        List<VisionTarget> snapped = new(targets.Count);
        foreach (VisionTarget target in targets)
        {
            PhysicalScreenBounds inScreen = OverlayGeometry.ToScreenBounds(target.Bounds, monitorBounds);
            UiElement? match = FindBestMatch(inScreen, elements);

            if (match is null)
            {
                snapped.Add(target);
                continue;
            }

            // Back into screenshot pixels, which is the space the overlay and the model share.
            PhysicalScreenBounds refined = new(
                match.Bounds.X - monitorBounds.X,
                match.Bounds.Y - monitorBounds.Y,
                match.Bounds.Width,
                match.Bounds.Height);

            // The model's label is what the user heard in the answer, so prefer it and fall
            // back to the element's accessible name only when the model gave none.
            string label = string.IsNullOrWhiteSpace(target.Label) ? match.Name : target.Label;
            snapped.Add(new VisionTarget(label, refined));
        }

        return snapped;
    }

    private static UiElement? FindBestMatch(
        PhysicalScreenBounds target,
        IReadOnlyList<UiElement> elements)
    {
        UiElement? best = null;
        double bestOverlap = 0;

        foreach (UiElement element in elements)
        {
            double overlap = IntersectionOverUnion(target, element.Bounds);
            if (overlap < MinimumOverlap)
            {
                continue;
            }

            // Higher overlap wins; on a tie prefer the smaller element, which is the more
            // specific one when a control is nested inside a container.
            if (overlap > bestOverlap
                || (overlap == bestOverlap && best is not null && Area(element.Bounds) < Area(best.Bounds)))
            {
                best = element;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    private static double IntersectionOverUnion(PhysicalScreenBounds a, PhysicalScreenBounds b)
    {
        long left = Math.Max(a.X, b.X);
        long top = Math.Max(a.Y, b.Y);
        long right = Math.Min((long)a.X + a.Width, (long)b.X + b.Width);
        long bottom = Math.Min((long)a.Y + a.Height, (long)b.Y + b.Height);

        if (right <= left || bottom <= top)
        {
            return 0;
        }

        double intersection = (double)(right - left) * (bottom - top);
        double union = Area(a) + Area(b) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static double Area(PhysicalScreenBounds bounds) => (double)bounds.Width * bounds.Height;
}
