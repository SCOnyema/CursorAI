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
    TimeSpan? Latency = null);

/// <summary>
/// Token accounting for one call. Every field is nullable: providers differ in what they
/// report, and an absent count must not be confused with zero.
/// </summary>
public sealed record VisionUsage(
    int? TotalTokens = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? ThoughtTokens = null);
