using CursorAI.App.Capture;
using CursorAI.App.Vision;

namespace CursorAI.Tests;

public class VisionTargetParserTests
{
    [Fact]
    public void TryParse_ConvertsTheMeasuredNormalisedPayloadIntoPixels()
    {
        // box_2d values captured from a real gemini-3.8-flash reply on 2026-10-12, against a
        // synthetic 1920x1080 screen whose Export PDF button was truly at (300,400,200,60).
        // That reply scored mean IoU 0.91 with centres within 2 pixels.
        const string text = """{"answer":"The Export PDF button.","targets":[{"label":"Export PDF","box_2d":[369,155,429,262]}]}""";

        VisionTargetPayload? payload = VisionTargetParser.TryParse(text, 1920, 1080);

        Assert.NotNull(payload);
        Assert.Equal("The Export PDF button.", payload!.Answer);
        VisionTarget target = Assert.Single(payload.Targets);
        Assert.Equal("Export PDF", target.Label);
        Assert.Equal(new PhysicalScreenBounds(298, 399, 205, 64), target.Bounds);
    }

    [Fact]
    public void TryParse_ReadsBoxesYFirst()
    {
        // box_2d is [ymin, xmin, ymax, xmax]. Reading it x-first transposes every target
        // across the diagonal, which looks plausible on a square box and wrong on anything else.
        const string text = """{"answer":"a","targets":[{"label":"wide","box_2d":[100,0,200,1000]}]}""";

        VisionTarget target = Assert.Single(VisionTargetParser.TryParse(text, 1000, 1000)!.Targets);

        // y 100-200 and x 0-1000 means short and full width, not tall and narrow.
        Assert.Equal(new PhysicalScreenBounds(0, 100, 1000, 100), target.Bounds);
    }

    [Fact]
    public void TryParse_ToleratesProseAndCodeFencesAroundTheJson()
    {
        const string text = "Sure, here you go:\n```json\n" +
            """{"answer":"ok","targets":[{"label":"X","box_2d":[0,0,100,100]}]}""" + "\n```\nHope that helps.";

        VisionTargetPayload? payload = VisionTargetParser.TryParse(text, 1000, 1000);

        Assert.NotNull(payload);
        Assert.Equal("ok", payload!.Answer);
        Assert.Single(payload.Targets);
    }

    [Fact]
    public void TryParse_OnOrdinaryProse_ReturnsNull()
    {
        // The normal case for a plain description: not a failure.
        Assert.Null(VisionTargetParser.TryParse(
            "You are looking at File Explorer. Your cursor is over Rename Folder.", 1920, 1080));
    }

    [Fact]
    public void TryParse_OnProseContainingBraces_ReturnsNull()
    {
        Assert.Null(VisionTargetParser.TryParse(
            "The config shows { \"debug\": true } on screen.", 1920, 1080));
    }

    [Fact]
    public void TryParse_SkipsMalformedTargetsButKeepsGoodOnes()
    {
        const string text = """
            {"answer":"two of four","targets":[
            {"label":"good","box_2d":[0,0,100,100]},
            {"label":"wrong length","box_2d":[0,0,100]},
            {"label":"not numbers","box_2d":["a","b","c","d"]},
            {"label":"degenerate","box_2d":[500,500,500,500]},
            {"label":"also good","box_2d":[200,200,300,300]}]}
            """;

        VisionTargetPayload? payload = VisionTargetParser.TryParse(text, 1000, 1000);

        Assert.NotNull(payload);
        Assert.Equal(["good", "also good"], payload!.Targets.Select(t => t.Label));
    }

    [Fact]
    public void TryParse_ClampsBoxesThatOverreachTheImage()
    {
        // A normalised value above 1000 would otherwise place guidance off-monitor.
        const string text = """{"answer":"a","targets":[{"label":"X","box_2d":[900,900,1400,1400]}]}""";

        VisionTarget target = Assert.Single(VisionTargetParser.TryParse(text, 1000, 1000)!.Targets);

        Assert.Equal(new PhysicalScreenBounds(900, 900, 100, 100), target.Bounds);
    }

    [Fact]
    public void TryParse_WhenAnswerIsMissing_FallsBackToTheRawText()
    {
        const string text = """{"targets":[{"label":"X","box_2d":[0,0,10,10]}]}""";

        VisionTargetPayload? payload = VisionTargetParser.TryParse(text, 1000, 1000);

        Assert.NotNull(payload);
        Assert.Contains("targets", payload!.Answer);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    public void TryParse_WithoutImageDimensions_ReturnsNull(int width, int height)
    {
        const string text = """{"answer":"a","targets":[{"label":"X","box_2d":[0,0,10,10]}]}""";

        Assert.Null(VisionTargetParser.TryParse(text, width, height));
    }
}
