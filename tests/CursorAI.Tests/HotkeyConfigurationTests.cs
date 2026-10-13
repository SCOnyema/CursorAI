using CursorAI.App.Input;

namespace CursorAI.Tests;

public class HotkeyConfigurationTests
{
    [Fact]
    public void Resolve_WithNothingConfigured_UsesTheDocumentedDefaults()
    {
        HotkeyConfiguration configuration = HotkeyConfiguration.Resolve(_ => null);

        Assert.Equal("Ctrl + Alt + A", configuration.Activation.DisplayName);
        Assert.Equal("Ctrl + Alt + S", configuration.Capture.DisplayName);
        Assert.Equal("Ctrl + Alt + D", configuration.Vision.DisplayName);
        Assert.Equal("Ctrl + Alt + Q", configuration.Quit.DisplayName);
        Assert.Equal("Ctrl + Alt + T", configuration.Type.DisplayName);
    }

    [Fact]
    public void Resolve_ResolvesTheTypeShortcutIndependently()
    {
        HotkeyConfiguration configuration = HotkeyConfiguration.Resolve(name =>
            name == HotkeyConfiguration.TypeVariable ? "Win + Alt + K" : null);

        Assert.Equal("Win + Alt + K", configuration.Type.DisplayName);
        Assert.Equal("Ctrl + Alt + A", configuration.Activation.DisplayName);
    }

    [Fact]
    public void Resolve_ResolvesTheQuitShortcutIndependently()
    {
        HotkeyConfiguration configuration = HotkeyConfiguration.Resolve(name =>
            name == HotkeyConfiguration.QuitVariable ? "Ctrl + Shift + F12" : null);

        Assert.Equal("Ctrl + Shift + F12", configuration.Quit.DisplayName);
        Assert.Equal("Ctrl + Alt + A", configuration.Activation.DisplayName);
    }

    [Fact]
    public void QuitShortcut_IsDistinctFromEveryOtherShortcut()
    {
        // Registering the same combination twice would silently lose one of them.
        HotkeyConfiguration c = HotkeyConfiguration.Resolve(_ => null);
        string[] names =
        [
            c.Activation.DisplayName, c.Capture.DisplayName, c.Vision.DisplayName,
            c.Quit.DisplayName, c.Type.DisplayName
        ];

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void Defaults_AreThreeKeyCombinationsRatherThanFourKeyChords()
    {
        HotkeyConfiguration configuration = HotkeyConfiguration.Resolve(_ => null);

        foreach (HotkeyDefinition hotkey in
            new[]
            {
                configuration.Activation, configuration.Capture, configuration.Vision,
                configuration.Quit, configuration.Type
            })
        {
            Assert.DoesNotContain("Shift", hotkey.DisplayName);
        }
    }

    [Fact]
    public void Defaults_DoNotUseCtrlAltSpace_WhichIsCommonlyAlreadyOwned()
    {
        // Measured on the development machine: Ctrl + Alt + Space was taken by another process
        // and it is exactly the combination a voice shortcut needs.
        HotkeyConfiguration configuration = HotkeyConfiguration.Resolve(_ => null);

        Assert.NotEqual("Ctrl + Alt + Space", configuration.Activation.DisplayName);
    }

    [Fact]
    public void Resolve_OverridesOnlyWhatIsConfigured()
    {
        // The real motivation: Ctrl + Alt + Space is owned by another process on the
        // development machine, so the activation shortcut must be movable on its own.
        HotkeyConfiguration configuration = HotkeyConfiguration.Resolve(name =>
            name == HotkeyConfiguration.ActivationVariable ? "Ctrl + Shift + J" : null);

        Assert.Equal("Ctrl + Shift + J", configuration.Activation.DisplayName);
        Assert.Equal("Ctrl + Alt + S", configuration.Capture.DisplayName);
        Assert.Equal("Ctrl + Alt + D", configuration.Vision.DisplayName);
    }

    [Fact]
    public void Resolve_WhenTextIsMalformed_NamesTheVariableAtFault()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => HotkeyConfiguration.Resolve(name =>
                name == HotkeyConfiguration.VisionVariable ? "Ctrl+Alt" : null));

        Assert.Contains(HotkeyConfiguration.VisionVariable, exception.Message);
        Assert.Contains("Ctrl + Alt + D", exception.Message);
        Assert.IsType<FormatException>(exception.InnerException);
    }
}
