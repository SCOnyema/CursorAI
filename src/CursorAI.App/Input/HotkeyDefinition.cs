namespace CursorAI.App.Input;

/// <summary>
/// One global shortcut, resolved from configuration into the values RegisterHotKey needs.
/// </summary>
/// <param name="Modifiers">MOD_* flags, excluding MOD_NOREPEAT which registration adds.</param>
/// <param name="VirtualKey">Virtual-key code.</param>
/// <param name="DisplayName">Canonical text form, used in diagnostics.</param>
internal sealed record HotkeyDefinition(uint Modifiers, uint VirtualKey, string DisplayName);
