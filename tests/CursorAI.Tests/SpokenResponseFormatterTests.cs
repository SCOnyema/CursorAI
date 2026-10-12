using CursorAI.App.Speech;

namespace CursorAI.Tests;

public class SpokenResponseFormatterTests
{
    [Fact]
    public void Format_StripsMarkdownSoItIsNotPronouncedLiterally()
    {
        // A synthesiser reads "**Export PDF**" as "star star Export PDF star star".
        string spoken = SpokenResponseFormatter.Format(
            "Your cursor is over the green **Export PDF** button, just above *Log in*.");

        Assert.Equal(
            "Your cursor is over the green Export PDF button, just above Log in.",
            spoken);
    }

    [Fact]
    public void Format_FlattensHeadingsBulletsAndInlineCode()
    {
        string spoken = SpokenResponseFormatter.Format(
            "### What Is Under Your Cursor\n- the `Rename` item\n- above Delete\n1. first\n");

        Assert.Equal("What Is Under Your Cursor the Rename item above Delete first", spoken);
    }

    [Fact]
    public void Format_RemovesFencedCodeBlocks()
    {
        string spoken = SpokenResponseFormatter.Format("Run this:\n```\ndotnet build\n```\nThen retry.");

        Assert.DoesNotContain("dotnet build", spoken);
        Assert.Contains("Run this:", spoken);
        Assert.Contains("Then retry.", spoken);
    }

    [Fact]
    public void Format_TruncatesAtASentenceBoundary()
    {
        string first = new('a', 80);
        string text = $"{first}. And then a great deal more text that nobody wants read aloud.";

        string spoken = SpokenResponseFormatter.Format(text, maximumCharacters: 100);

        Assert.Equal($"{first}.", spoken);
    }

    [Fact]
    public void Format_WhenThereIsNoSentenceBoundary_CutsAtAWordAndMarksIt()
    {
        string text = string.Join(' ', Enumerable.Repeat("alpha", 40));

        string spoken = SpokenResponseFormatter.Format(text, maximumCharacters: 50);

        Assert.True(spoken.Length <= 51, $"length was {spoken.Length}");
        Assert.EndsWith("\u2026", spoken);
        Assert.DoesNotContain("alph\u2026", spoken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_WithNothingToSay_ReturnsEmpty(string? text)
    {
        Assert.Equal(string.Empty, SpokenResponseFormatter.Format(text));
    }

    [Fact]
    public void Format_LeavesPlainProseUnchanged()
    {
        const string text = "You are looking at File Explorer. Your cursor is over Rename Folder.";

        Assert.Equal(text, SpokenResponseFormatter.Format(text));
    }
}
