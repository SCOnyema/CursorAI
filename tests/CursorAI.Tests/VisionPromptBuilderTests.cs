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

    [Fact]
    public void Build_TellsTheModelToAnswerTheQuestionRatherThanNarrateTheScreen()
    {
        // Without this the model opened every answer by describing the screen and the cursor.
        string prompt = VisionPromptBuilder.Build(CreateRequest("How do I export?", Foreground));

        Assert.Contains("You are CursorAI", prompt);
        Assert.Contains("Do not open by describing", prompt);
        Assert.Contains("numbered steps", prompt);
    }

    [Fact]
    public void Build_OmitsMonitorGeometry()
    {
        string prompt = VisionPromptBuilder.Build(CreateRequest("Anything", Foreground));

        Assert.DoesNotContain("Monitor bounds", prompt);
        Assert.DoesNotContain("work area", prompt);
    }

    [Fact]
    public void Build_WithoutHistory_HasNoConversationSection()
    {
        string prompt = VisionPromptBuilder.Build(CreateRequest("Anything", Foreground));

        Assert.DoesNotContain("Earlier in this conversation", prompt);
    }

    [Fact]
    public void Build_WithHistory_IncludesEarlierTurnsOldestFirstBeforeTheQuestion()
    {
        ConversationTurn[] history =
        [
            new("How do I export this?", "Open File, then choose Export.", DateTimeOffset.UnixEpoch),
            new("Which format?", "PDF keeps the layout.", DateTimeOffset.UnixEpoch)
        ];

        string prompt = VisionPromptBuilder.Build(
            CreateRequest("And then what?", Foreground, history));

        int first = prompt.IndexOf("User: How do I export this?", StringComparison.Ordinal);
        int second = prompt.IndexOf("User: Which format?", StringComparison.Ordinal);
        int question = prompt.LastIndexOf("And then what?", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < second && second < question);
        Assert.Contains("CursorAI: PDF keeps the layout.", prompt);
        Assert.Contains("trust the current", prompt);
        Assert.EndsWith("And then what?", prompt);
    }

    [Fact]
    public void Build_WithTargets_NoLongerCapsTheAnswerAtTwoSentences()
    {
        string prompt = VisionPromptBuilder.Build(
            CreateRequest("How do I export?", Foreground, requestsTargets: true));

        Assert.DoesNotContain("one or two sentences", prompt);
        Assert.Contains("box_2d", prompt);
    }

    private static readonly ForegroundWindowInfo Foreground = new(1, "Report.docx - Word", "WINWORD");

    private static VisionRequest CreateRequest(
        string question,
        ForegroundWindowInfo foregroundWindow,
        IReadOnlyList<ConversationTurn>? history = null,
        bool requestsTargets = false)
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
            foregroundWindow,
            requestsTargets,
            history);
    }
}
