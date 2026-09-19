namespace CursorAI.App.Vision;

public interface IVisionProvider
{
    Task<VisionResult> AnalyzeAsync(VisionRequest request, CancellationToken cancellationToken);
}
