using CursorAI.App.Capture;

namespace CursorAI.App.Vision;

/// <summary>
/// Provider-neutral result of one vision analysis. Only <see cref="ResponseText"/> is
/// guaranteed; the rest is best-effort metadata a provider may not report.
/// </summary>
/// <param name="ResponseText">The model's answer.</param>
/// <param name="Model">Model that produced the answer, as reported by the provider.</param>
/// <param name="Usage">Token accounting, where the provider reports it.</param>
/// <param name="Latency">
/// Wall-clock duration of the call, measured by <see cref="VisionService"/> rather than by a
/// provider so it is comparable across providers.
/// </param>
public sealed record VisionResult(
    string ResponseText,
    string? Model = null,
    VisionUsage? Usage = null,
    TimeSpan? Latency = null,
    IReadOnlyList<VisionTarget>? Targets = null);

/// <summary>
/// One on-screen element the model located, for visual guidance to point at.
/// </summary>
/// <param name="Label">The element's visible text or a short description of it.</param>
/// <param name="Bounds">
/// Position in <em>screenshot</em> pixels, origin at the top-left of the captured image.
/// Providers convert from whatever convention they report natively, so nothing above
/// <see cref="IVisionProvider"/> has to know about vendor coordinate formats.
/// </param>
public sealed record VisionTarget(string Label, PhysicalScreenBounds Bounds);

/// <summary>
/// Token accounting for one call. Every field is nullable: providers differ in what they
/// report, and an absent count must not be confused with zero.
/// </summary>
public sealed record VisionUsage(
    int? TotalTokens = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? ThoughtTokens = null);
