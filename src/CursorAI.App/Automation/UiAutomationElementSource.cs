using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using CursorAI.App.Capture;

namespace CursorAI.App.Automation;

/// <summary>
/// Reads window contents through Windows UI Automation.
/// </summary>
/// <remarks>
/// Measured 2026-10-12 on the development machine:
///   FromHandle plus a full descendant walk of Notepad: 45 elements, 33 named, about 150 ms.
///   The same against an Electron window: three window-chrome buttons and nothing else.
///   Locating a window by enumerating the desktop's children: 1795 ms, which is why the
///   foreground window handle CursorAI already captures is used instead.
///   First UI Automation call in a process costs about 130 ms of COM warm-up; later calls 5-7 ms.
/// Every read is defensive: elements disappear between being enumerated and being inspected,
/// which surfaces as COM exceptions that must not reach the user.
/// </remarks>
internal sealed class UiAutomationElementSource : IUiElementSource
{
    /// <summary>
    /// Upper bound on elements inspected. A complex window can expose thousands, and guidance
    /// does not improve past the first few hundred candidates.
    /// </summary>
    internal const int MaximumElements = 400;

    public IReadOnlyList<UiElement> GetElements(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return [];
        }

        try
        {
            AutomationElement? window = AutomationElement.FromHandle(windowHandle);
            if (window is null)
            {
                return [];
            }

            AutomationElementCollection found = window.FindAll(
                TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);

            List<UiElement> elements = [];
            foreach (AutomationElement element in found)
            {
                if (elements.Count >= MaximumElements)
                {
                    break;
                }

                if (TryRead(element) is { } read)
                {
                    elements.Add(read);
                }
            }

            return elements;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException
            or TimeoutException)
        {
            // A window that closed mid-walk, or a provider that refuses to answer. Guidance
            // falls back to the model's own coordinates.
            Debug.WriteLine($"UI Automation read failed: {exception.GetType().Name}: {exception.Message}");
            return [];
        }
    }

    private static UiElement? TryRead(AutomationElement element)
    {
        try
        {
            AutomationElement.AutomationElementInformation info = element.Current;
            if (info.IsOffscreen)
            {
                return null;
            }

            Rect rect = info.BoundingRectangle;
            if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1
                || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height))
            {
                return null;
            }

            return new UiElement(
                info.Name ?? string.Empty,
                info.ControlType?.ProgrammaticName?.Replace("ControlType.", string.Empty) ?? string.Empty,
                new PhysicalScreenBounds(
                    (int)Math.Round(rect.X),
                    (int)Math.Round(rect.Y),
                    (int)Math.Round(rect.Width),
                    (int)Math.Round(rect.Height)),
                info.IsEnabled);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
            or System.Runtime.InteropServices.COMException)
        {
            // Normal: the element went away between enumeration and inspection.
            return null;
        }
    }
}
