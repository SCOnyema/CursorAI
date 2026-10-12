using System.Globalization;

namespace CursorAI.App.Input;

/// <summary>
/// Parses shortcut text such as "Ctrl + Alt + Shift + S" into a <see cref="HotkeyDefinition"/>.
/// </summary>
/// <remarks>
/// Shortcuts are configurable because global hotkeys are a shared, first-come resource.
/// Verified 2026-10-08: Ctrl + Alt + Space was already owned by another process on the
/// development machine, which is precisely the combination V0.4 push-to-talk needs. A
/// hardcoded default would ship the feature dead on that machine.
/// </remarks>
internal static class HotkeyParser
{
    private const uint AltModifier = 0x0001;
    private const uint ControlModifier = 0x0002;
    private const uint ShiftModifier = 0x0004;
    private const uint WindowsModifier = 0x0008;

    private static readonly Dictionary<string, uint> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = ControlModifier,
        ["control"] = ControlModifier,
        ["alt"] = AltModifier,
        ["shift"] = ShiftModifier,
        ["win"] = WindowsModifier,
        ["windows"] = WindowsModifier
    };

    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = 0x20,
        ["enter"] = 0x0D,
        ["return"] = 0x0D,
        ["tab"] = 0x09,
        ["escape"] = 0x1B,
        ["esc"] = 0x1B,
        ["backspace"] = 0x08,
        ["insert"] = 0x2D,
        ["delete"] = 0x2E,
        ["home"] = 0x24,
        ["end"] = 0x23,
        ["pageup"] = 0x21,
        ["pagedown"] = 0x22,
        ["left"] = 0x25,
        ["up"] = 0x26,
        ["right"] = 0x27,
        ["down"] = 0x28
    };

    /// <summary>
    /// Parses shortcut text. Accepts "+" or "-" separators and any spacing or casing.
    /// </summary>
    /// <exception cref="FormatException">
    /// The text names no key, names more than one, uses an unknown token, or has no modifier.
    /// </exception>
    internal static HotkeyDefinition Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("A hotkey must not be empty.");
        }

        string[] tokens = text.Split(['+', '-'], StringSplitOptions.TrimEntries
            | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new FormatException($"'{text}' does not name a hotkey.");
        }

        uint modifiers = 0;
        uint? virtualKey = null;
        List<string> modifierNames = [];
        string? keyName = null;

        foreach (string token in tokens)
        {
            if (Modifiers.TryGetValue(token, out uint modifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    throw new FormatException($"'{text}' repeats the modifier '{token}'.");
                }

                modifiers |= modifier;
                modifierNames.Add(Canonicalize(token));
                continue;
            }

            if (virtualKey is not null)
            {
                throw new FormatException(
                    $"'{text}' names more than one key ('{keyName}' and '{token}'). "
                    + "A hotkey needs exactly one key plus its modifiers.");
            }

            virtualKey = ResolveKey(token, text);
            keyName = Canonicalize(token);
        }

        if (virtualKey is null)
        {
            throw new FormatException($"'{text}' names only modifiers, with no key to press.");
        }

        if (modifiers == 0)
        {
            // A bare key would capture that key system-wide, which is never what is wanted.
            throw new FormatException(
                $"'{text}' has no modifier. A global hotkey must combine at least one of "
                + "Ctrl, Alt, Shift or Win with a key.");
        }

        modifierNames.Add(keyName!);
        return new HotkeyDefinition(modifiers, virtualKey.Value, string.Join(" + ", modifierNames));
    }

    /// <summary>Parses configured text, falling back to a default when unset or blank.</summary>
    internal static HotkeyDefinition ParseOrDefault(string? configured, string defaultText)
    {
        return Parse(string.IsNullOrWhiteSpace(configured) ? defaultText : configured);
    }

    private static uint ResolveKey(string token, string text)
    {
        if (NamedKeys.TryGetValue(token, out uint named))
        {
            return named;
        }

        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return c;
            }
        }

        if ((token[0] is 'f' or 'F')
            && uint.TryParse(token.AsSpan(1), CultureInfo.InvariantCulture, out uint functionKey)
            && functionKey is >= 1 and <= 24)
        {
            return 0x6F + functionKey; // VK_F1 is 0x70
        }

        throw new FormatException(
            $"'{text}' uses '{token}', which is not a recognised key or modifier.");
    }

    private static string Canonicalize(string token)
    {
        return token.Length == 1
            ? token.ToUpperInvariant()
            : string.Concat(char.ToUpperInvariant(token[0]), token[1..].ToLowerInvariant());
    }
}
