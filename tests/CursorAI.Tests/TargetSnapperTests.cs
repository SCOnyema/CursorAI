using CursorAI.App.Automation;
using CursorAI.App.Capture;
using CursorAI.App.Vision;

namespace CursorAI.Tests;

public class TargetSnapperTests
{
    private static readonly PhysicalScreenBounds Primary = new(0, 0, 1920, 1080);

    private static UiElement Element(string name, int x, int y, int w, int h, string type = "Button")
        => new(name, type, new PhysicalScreenBounds(x, y, w, h), IsEnabled: true);

    [Fact]
    public void Snap_ReplacesAnApproximateBoxWithTheExactElementBounds()
    {
        // The model is close but not exact; UI Automation knows the real rectangle.
        VisionTarget[] targets = [new("Export PDF", new PhysicalScreenBounds(298, 399, 205, 64))];
        UiElement[] elements = [Element("Export PDF", 300, 400, 200, 60)];

        VisionTarget snapped = Assert.Single(TargetSnapper.Snap(targets, elements, Primary));

        Assert.Equal(new PhysicalScreenBounds(300, 400, 200, 60), snapped.Bounds);
        Assert.Equal("Export PDF", snapped.Label);
    }

    [Fact]
    public void Snap_TranslatesThroughMonitorOriginBothWays()
    {
        // The case a single-display machine cannot exercise. Screenshot pixel (100,200) on a
        // monitor at X=-1920 is screen pixel (-1820,200); the refined result must come back in
        // screenshot pixels, not screen pixels.
        PhysicalScreenBounds secondary = new(-1920, 0, 1920, 1080);
        VisionTarget[] targets = [new("Save", new PhysicalScreenBounds(100, 200, 80, 30))];
        UiElement[] elements = [Element("Save", -1820, 200, 84, 32)];

        VisionTarget snapped = Assert.Single(TargetSnapper.Snap(targets, elements, secondary));

        Assert.Equal(new PhysicalScreenBounds(100, 200, 84, 32), snapped.Bounds);
    }

    [Fact]
    public void Snap_WithNoElements_KeepsModelCoordinatesUnchanged()
    {
        // Electron and Chromium windows expose nothing usable; guidance must not regress there.
        VisionTarget[] targets = [new("Run", new PhysicalScreenBounds(10, 20, 30, 40))];

        IReadOnlyList<VisionTarget> result = TargetSnapper.Snap(targets, [], Primary);

        Assert.Same(targets, result);
    }

    [Fact]
    public void Snap_IgnoresAPaneSpanningTheWholeWindow()
    {
        // Exactly what an Electron window reports: a nameless pane covering everything. Its
        // overlap with a button-sized box is under 0.01, far below the threshold.
        VisionTarget[] targets = [new("Export PDF", new PhysicalScreenBounds(300, 400, 200, 60))];
        UiElement[] elements = [Element("", 0, 0, 1920, 1020, "Pane")];

        VisionTarget snapped = Assert.Single(TargetSnapper.Snap(targets, elements, Primary));

        Assert.Equal(new PhysicalScreenBounds(300, 400, 200, 60), snapped.Bounds);
        Assert.Equal("Export PDF", snapped.Label);
    }

    [Fact]
    public void Snap_MatchesAButtonWhenTheModelBoxedOnlyItsLabelText()
    {
        // Model boxes the glyph "File" (52x20); UI Automation has the MenuItem (52x40).
        // Overlap is 0.5, which must qualify.
        VisionTarget[] targets = [new("File", new PhysicalScreenBounds(133, 182, 52, 20))];
        UiElement[] elements = [Element("File", 133, 172, 52, 40, "MenuItem")];

        VisionTarget snapped = Assert.Single(TargetSnapper.Snap(targets, elements, Primary));

        Assert.Equal(new PhysicalScreenBounds(133, 172, 52, 40), snapped.Bounds);
    }

    [Fact]
    public void Snap_PrefersTheHighestOverlapAmongCandidates()
    {
        VisionTarget[] targets = [new("Close", new PhysicalScreenBounds(1863, 0, 58, 43))];
        UiElement[] elements =
        [
            Element("Restore", 1805, 0, 58, 43),
            Element("Close", 1863, 0, 58, 43),
            Element("Title bar", 1700, 0, 220, 43),
        ];

        VisionTarget snapped = Assert.Single(TargetSnapper.Snap(targets, elements, Primary));

        Assert.Equal(new PhysicalScreenBounds(1863, 0, 58, 43), snapped.Bounds);
    }

    [Fact]
    public void Snap_OnEqualOverlap_PrefersTheMoreSpecificSmallerElement()
    {
        VisionTarget[] targets = [new("OK", new PhysicalScreenBounds(100, 100, 50, 50))];
        UiElement[] elements =
        [
            Element("container", 100, 100, 50, 50, "Pane"),
            Element("OK", 100, 100, 50, 50),
        ];

        VisionTarget snapped = Assert.Single(TargetSnapper.Snap(targets, elements, Primary));

        Assert.Equal(50 * 50, snapped.Bounds.Width * snapped.Bounds.Height);
    }

    [Fact]
    public void Snap_WhenTheModelGaveNoLabel_UsesTheElementName()
    {
        VisionTarget[] targets = [new("  ", new PhysicalScreenBounds(300, 400, 200, 60))];
        UiElement[] elements = [Element("Export PDF", 300, 400, 200, 60)];

        Assert.Equal("Export PDF", Assert.Single(TargetSnapper.Snap(targets, elements, Primary)).Label);
    }

    [Fact]
    public void Snap_LeavesUnmatchedTargetsAloneWhileRefiningOthers()
    {
        VisionTarget[] targets =
        [
            new("Export PDF", new PhysicalScreenBounds(298, 399, 205, 64)),
            new("Custom canvas thing", new PhysicalScreenBounds(50, 900, 100, 40)),
        ];
        UiElement[] elements = [Element("Export PDF", 300, 400, 200, 60)];

        IReadOnlyList<VisionTarget> result = TargetSnapper.Snap(targets, elements, Primary);

        Assert.Equal(new PhysicalScreenBounds(300, 400, 200, 60), result[0].Bounds);
        Assert.Equal(new PhysicalScreenBounds(50, 900, 100, 40), result[1].Bounds);
    }
}
