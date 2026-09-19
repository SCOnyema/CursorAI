namespace CursorAI.App.Capture;

public sealed record CapturedMonitorInfo(
    PhysicalScreenBounds Bounds,
    PhysicalScreenBounds WorkArea,
    bool IsPrimary);
