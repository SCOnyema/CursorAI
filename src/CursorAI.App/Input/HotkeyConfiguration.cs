namespace CursorAI.App.Input;

/// <summary>
/// The three shortcuts CursorAI registers, resolved from the process environment so a
/// conflict can be worked around without a rebuild. Consistent with how GEMINI_MODEL and
/// CURSORAI_VISION_PROVIDER are already configured.
/// </summary>
internal sealed record HotkeyConfiguration(
    HotkeyDefinition Activation,
    HotkeyDefinition Capture,
    HotkeyDefinition Vision,
    HotkeyDefinition Quit,
    HotkeyDefinition Type)
{
    internal const string ActivationVariable = "CURSORAI_HOTKEY_ACTIVATE";
    internal const string CaptureVariable = "CURSORAI_HOTKEY_CAPTURE";
    internal const string VisionVariable = "CURSORAI_HOTKEY_VISION";
    internal const string QuitVariable = "CURSORAI_HOTKEY_QUIT";
    internal const string TypeVariable = "CURSORAI_HOTKEY_TYPE";

    // Three keys on the home row rather than four-key chords, mnemonic (Ask / Screenshot /
    // Describe), and adjacent so each is one left-hand motion.
    //
    // Measured 2026-10-12 on the development machine: of 51 candidate combinations only
    // Ctrl + Alt + Space and Alt + F4 were already owned, and all 26 Ctrl + Alt + <letter>
    // combinations were free. Ctrl + Alt + Space was simply an unlucky default - input-method
    // switchers and launcher tools commonly claim it - and it is the combination voice needs.
    //
    // Caveat for non-US layouts: AltGr is Ctrl + Alt, so on keyboards that use AltGr to type
    // accented characters these can collide with ordinary typing. That is what the
    // CURSORAI_HOTKEY_* overrides are for.
    internal const string DefaultActivation = "Ctrl + Alt + A";
    internal const string DefaultCapture = "Ctrl + Alt + S";
    internal const string DefaultVision = "Ctrl + Alt + D";

    // The buddy window is click-through with no taskbar entry, so without this there is no way
    // to stop CursorAI at all. A forgotten instance locked CursorAI.App.exe and broke the build
    // twice in two sessions before this existed.
    internal const string DefaultQuit = "Ctrl + Alt + Q";

    // Typing is the fallback when dictation mishears, which it measurably does.
    internal const string DefaultType = "Ctrl + Alt + T";

    internal static HotkeyConfiguration FromEnvironment()
    {
        return Resolve(Environment.GetEnvironmentVariable);
    }

    /// <summary>
    /// Resolves each shortcut, reporting which variable is at fault when text is malformed.
    /// </summary>
    internal static HotkeyConfiguration Resolve(Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);

        return new HotkeyConfiguration(
            ResolveOne(readVariable, ActivationVariable, DefaultActivation),
            ResolveOne(readVariable, CaptureVariable, DefaultCapture),
            ResolveOne(readVariable, VisionVariable, DefaultVision),
            ResolveOne(readVariable, QuitVariable, DefaultQuit),
            ResolveOne(readVariable, TypeVariable, DefaultType));
    }

    private static HotkeyDefinition ResolveOne(
        Func<string, string?> readVariable,
        string variable,
        string defaultText)
    {
        string? configured = readVariable(variable);

        try
        {
            return HotkeyParser.ParseOrDefault(configured, defaultText);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"{variable} is not a valid hotkey. {exception.Message} "
                + $"Leave it unset to use '{defaultText}'.",
                exception);
        }
    }
}
