using CursorAI.App.Input;

namespace CursorAI.Tests;

public class HotkeyParserTests
{
    private const uint Alt = 0x0001;
    private const uint Control = 0x0002;
    private const uint Shift = 0x0004;
    private const uint Windows = 0x0008;

    [Fact]
    public void Parse_ReadsModifiersAndKey()
    {
        HotkeyDefinition hotkey = HotkeyParser.Parse("Ctrl + Alt + Shift + S");

        Assert.Equal(Control | Alt | Shift, hotkey.Modifiers);
        Assert.Equal(0x53u, hotkey.VirtualKey);
        Assert.Equal("Ctrl + Alt + Shift + S", hotkey.DisplayName);
    }

    [Theory]
    [InlineData("ctrl+alt+space")]
    [InlineData("  CONTROL - ALT - Space  ")]
    [InlineData("Alt+Ctrl+Space")]
    public void Parse_IgnoresCasingSpacingAndSeparatorStyle(string text)
    {
        HotkeyDefinition hotkey = HotkeyParser.Parse(text);

        Assert.Equal(Control | Alt, hotkey.Modifiers);
        Assert.Equal(0x20u, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("Ctrl+F1", 0x70u)]
    [InlineData("Ctrl+F12", 0x7Bu)]
    [InlineData("Ctrl+F24", 0x87u)]
    [InlineData("Ctrl+7", (uint)'7')]
    [InlineData("Win+Delete", 0x2Eu)]
    [InlineData("Ctrl+PageDown", 0x22u)]
    public void Parse_SupportsFunctionDigitAndNamedKeys(string text, uint expectedKey)
    {
        Assert.Equal(expectedKey, HotkeyParser.Parse(text).VirtualKey);
    }

    [Fact]
    public void Parse_SupportsWindowsModifier()
    {
        Assert.Equal(Windows | Shift, HotkeyParser.Parse("Win+Shift+Q").Modifiers);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("Ctrl+Alt", "no key")]
    [InlineData("S", "no modifier")]
    [InlineData("Ctrl+A+B", "two keys")]
    [InlineData("Ctrl+Ctrl+A", "repeated modifier")]
    [InlineData("Ctrl+Nonsense", "unknown token")]
    [InlineData("Ctrl+F25", "function key out of range")]
    public void Parse_RejectsMalformedText(string text, string why)
    {
        Assert.ThrowsAny<FormatException>(() => HotkeyParser.Parse(text));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public void Parse_RejectsABareKey_WhichWouldCaptureItSystemWide()
    {
        FormatException exception = Assert.Throws<FormatException>(() => HotkeyParser.Parse("F5"));

        Assert.Contains("no modifier", exception.Message);
    }

    [Fact]
    public void ParseOrDefault_FallsBackWhenUnsetOrBlank()
    {
        Assert.Equal("Ctrl + Alt + Space", HotkeyParser.ParseOrDefault(null, "Ctrl + Alt + Space").DisplayName);
        Assert.Equal("Ctrl + Alt + Space", HotkeyParser.ParseOrDefault("   ", "Ctrl + Alt + Space").DisplayName);
        Assert.Equal("Ctrl + Shift + K", HotkeyParser.ParseOrDefault("ctrl+shift+k", "Ctrl + Alt + Space").DisplayName);
    }
}
