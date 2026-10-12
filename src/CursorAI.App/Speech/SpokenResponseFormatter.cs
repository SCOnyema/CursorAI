using System.Text;
using System.Text.RegularExpressions;

namespace CursorAI.App.Speech;

/// <summary>
/// Turns a vision response into something worth hearing. Model answers are written to be read:
/// they carry markdown emphasis, bullet lists and headings that a synthesiser pronounces
/// literally ("star star Export PDF star star"), and they can run far longer than anyone wants
/// read aloud.
/// </summary>
internal static partial class SpokenResponseFormatter
{
    internal const int DefaultMaximumCharacters = 600;

    internal static string Format(string? responseText, int maximumCharacters = DefaultMaximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        string text = responseText;
        text = CodeFencePattern().Replace(text, " ");
        // $1$2: the bold and italic alternatives capture into different groups, and only
        // one of them participates in any given match.
        text = EmphasisPattern().Replace(text, "$1$2");
        text = InlineCodePattern().Replace(text, "$1");
        text = HeadingPattern().Replace(text, string.Empty);
        text = BulletPattern().Replace(text, string.Empty);
        text = WhitespacePattern().Replace(text, " ").Trim();

        return text.Length <= maximumCharacters ? text : Truncate(text, maximumCharacters);
    }

    /// <summary>Cuts at a sentence boundary where possible, so speech does not stop mid-word.</summary>
    private static string Truncate(string text, int maximumCharacters)
    {
        ReadOnlySpan<char> window = text.AsSpan(0, maximumCharacters);
        int sentenceEnd = window.LastIndexOfAny('.', '!', '?');
        if (sentenceEnd >= maximumCharacters / 2)
        {
            return text[..(sentenceEnd + 1)];
        }

        int wordEnd = window.LastIndexOf(' ');
        StringBuilder builder = new(text[..(wordEnd > 0 ? wordEnd : maximumCharacters)].TrimEnd());
        builder.Append('\u2026');
        return builder.ToString();
    }

    [GeneratedRegex(@"```.*?```", RegexOptions.Singleline)]
    private static partial Regex CodeFencePattern();

    // Markdown emphasis: **bold**, *italic*, __bold__, _italic_.
    [GeneratedRegex(@"(?:\*\*|__)(.+?)(?:\*\*|__)|(?:\*|_)([^*_\n]+?)(?:\*|_)")]
    private static partial Regex EmphasisPattern();

    [GeneratedRegex("`([^`\n]+)`")]
    private static partial Regex InlineCodePattern();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s*(?:[-*+]|\d+\.)\s+", RegexOptions.Multiline)]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
