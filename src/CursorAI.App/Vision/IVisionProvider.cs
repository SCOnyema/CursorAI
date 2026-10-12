namespace CursorAI.App.Vision;

/// <summary>
/// Provider-neutral vision boundary. Implementations own their own transport or SDK types,
/// model identifier, credential lookup, request translation, and response extraction.
/// Nothing above this interface may reference a vendor type.
/// </summary>
/// <remarks>
/// Disposal is part of the contract so a composition root can hold an
/// <see cref="IVisionProvider"/> rather than a concrete type purely to release a transport.
/// Implementations with nothing to release should implement it as a no-op.
/// </remarks>
public interface IVisionProvider : IDisposable
{
    /// <summary>Human-readable provider and model, for diagnostics and logging.</summary>
    string Description { get; }

    Task<VisionResult> AnalyzeAsync(VisionRequest request, CancellationToken cancellationToken);
}
