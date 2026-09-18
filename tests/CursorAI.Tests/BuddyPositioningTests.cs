using System.Windows;
using CursorAI.App;

namespace CursorAI.Tests;

public class BuddyPositioningTests
{
    [Fact]
    public void Calculate_AppliesCursorOffsetInsideWorkArea()
    {
        Point result = BuddyPositioning.Calculate(
            new Point(100, 150),
            new Rect(0, 0, 1920, 1080),
            new Size(64, 64),
            new Vector(16, 16));

        Assert.Equal(new Point(116, 166), result);
    }

    [Fact]
    public void Calculate_KeepsBuddyInsideRightAndBottomEdges()
    {
        Point result = BuddyPositioning.Calculate(
            new Point(1900, 1060),
            new Rect(0, 0, 1920, 1080),
            new Size(64, 64),
            new Vector(16, 16));

        Assert.Equal(new Point(1856, 1016), result);
    }

    [Fact]
    public void Calculate_HandlesWorkAreasWithNegativeCoordinates()
    {
        Point result = BuddyPositioning.Calculate(
            new Point(-1915, 20),
            new Rect(-1920, 0, 1920, 1080),
            new Size(64, 64),
            new Vector(-16, -16));

        Assert.Equal(new Point(-1920, 4), result);
    }
}
