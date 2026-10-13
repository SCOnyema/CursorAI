using CursorAI.App.Capture;

namespace CursorAI.App.Automation;

/// <summary>
/// One element reported by Windows UI Automation, in physical screen pixels.
/// </summary>
/// <param name="Name">The element's accessible name, often its visible text.</param>
/// <param name="ControlType">Programmatic control type, e.g. "Button" or "MenuItem".</param>
/// <param name="Bounds">Position in physical screen pixels, not screenshot pixels.</param>
/// <param name="IsEnabled">False for elements present but not currently actionable.</param>
public sealed record UiElement(
    string Name,
    string ControlType,
    PhysicalScreenBounds Bounds,
    bool IsEnabled);
