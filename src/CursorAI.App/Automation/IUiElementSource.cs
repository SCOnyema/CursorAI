namespace CursorAI.App.Automation;

/// <summary>
/// Supplies the UI Automation elements of one window, so guidance can be snapped to real
/// element bounds instead of a model's estimate.
/// </summary>
/// <remarks>
/// An interface because the real implementation cannot be unit tested: it reads whatever
/// happens to be on screen. Coverage also varies enormously by application, so returning an
/// empty list is a normal outcome rather than a failure. Measured 2026-10-12: Notepad exposed
/// 45 elements with 33 named, while an Electron window exposed only its three window-chrome
/// buttons behind nameless panes spanning the whole window.
/// </remarks>
internal interface IUiElementSource
{
    /// <summary>
    /// Returns the actionable elements of the given window, or an empty list when the window
    /// exposes nothing usable.
    /// </summary>
    IReadOnlyList<UiElement> GetElements(nint windowHandle);
}
