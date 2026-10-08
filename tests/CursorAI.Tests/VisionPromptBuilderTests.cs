using CursorAI.App.Capture;
using CursorAI.App.Vision;

namespace CursorAI.Tests;

public class VisionPromptBuilderTests
{
    [Fact]
    public void Build_IncludesScreenContextAndExactUserQuestion()
    {
        const string question = "What am I looking at, and what is under my cursor?";
        VisionRequest request = CreateRequest(
            question,
            new ForegroundWindowInfo(123, "Mercor | Home - Google Chrome", "chrome"));

        string prompt = VisionPromptBuilder.Build(request);

        Assert.Contains("1920x1080", prompt);
        Assert.Contains("(672, 296)", prompt);
        Assert.Contains("Foreground application: chrome.", prompt);
        Assert.Contains("Foreground window title: Mercor | Home - Google Chrome.", prompt);
        Assert.EndsWith(question, prompt);
    }

    [Fact]
    public void Build_RepresentsUnavailableForegroundMetadataClearly()
    {
        VisionRequest request = CreateRequest(
            "Describe the screen",
            new ForegroundWindowInfo(0, null, null));

        string prompt = VisionPromptBuilder.Build(request);

        Assert.Contains("Foreground application: <unavailable>.", prompt);
        Assert.Contains("Foreground window title: <unavailable>.", prompt);
        Assert.EndsWith("Describe the screen", prompt);
    }

    private static VisionRequest CreateRequest(string question, ForegroundWindowInfo foregroundWindow)
    {
        return new VisionRequest(
            question,
            new byte[] { 1, 2, 3 },
            1920,
            1080,
            DateTimeOffset.UnixEpoch,
            new CapturedMonitorInfo(
                new PhysicalScreenBounds(0, 0, 1920, 1080),
                new PhysicalScreenBounds(0, 0, 1920, 1040),
                true),
            new PhysicalScreenPoint(672, 296),
            new PhysicalScreenPoint(672, 296),
            foregroundWindow);
    }
}
