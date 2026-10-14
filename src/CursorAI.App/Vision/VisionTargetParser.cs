using System.Text.Json;
using CursorAI.App.Capture;

namespace CursorAI.App.Vision;

/// <summary>
/// Reads the guidance payload out of a model reply and converts its boxes into screenshot
/// pixels, so nothing above <see cref="IVisionProvider"/> sees a coordinate convention.
/// </summary>
/// <remarks>
/// Shared by every provider, because the format it parses is the one
/// <see cref="VisionPromptBuilder"/> asks for rather than anything a vendor defines. Naming it
/// after one vendor implied otherwise.
/// </remarks>
/// <remarks>
/// Gemini reports boxes as <c>box_2d: [ymin, xmin, ymax, xmax]</c> normalised to 0-1000.
/// Measured 2026-10-12 against synthetic screens with known rectangles: that convention gave
/// mean IoU 0.91 with centres within 2 pixels, where asking for raw pixels gave mean IoU 0.24
/// with centres 29 to 54 pixels out. The ordering is y-first, which is easy to get backwards
/// and produces targets transposed across the diagonal when it happens.
/// </remarks>
internal static class VisionTargetParser
{
    private const double NormalizedScale = 1000.0;

    /// <summary>
    /// Returns the parsed payload, or null when the reply is ordinary prose. A reply that was
    /// never asked for targets is the normal case, not a failure.
    /// </summary>
    internal static VisionTargetPayload? TryParse(string responseText, int imageWidth, int imageHeight)
    {
        if (string.IsNullOrWhiteSpace(responseText) || imageWidth <= 0 || imageHeight <= 0)
        {
            return null;
        }

        // Models wrap JSON in prose or a code fence despite instructions, so work from the
        // outermost braces rather than requiring the whole reply to be JSON.
        int start = responseText.IndexOf('{');
        int end = responseText.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText[start..(end + 1)]);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("targets", out JsonElement targets)
                || targets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string answer = root.TryGetProperty("answer", out JsonElement a)
                && a.ValueKind == JsonValueKind.String
                    ? a.GetString() ?? string.Empty
                    : string.Empty;

            List<VisionTarget> parsed = [];
            foreach (JsonElement target in targets.EnumerateArray())
            {
                if (TryReadTarget(target, imageWidth, imageHeight) is { } visionTarget)
                {
                    parsed.Add(visionTarget);
                }
            }

            return new VisionTargetPayload(
                string.IsNullOrWhiteSpace(answer) ? responseText.Trim() : answer,
                parsed);
        }
        catch (JsonException)
        {
            // Prose that merely contains braces. The caller falls back to the raw text.
            return null;
        }
    }

    private static VisionTarget? TryReadTarget(JsonElement target, int imageWidth, int imageHeight)
    {
        if (target.ValueKind != JsonValueKind.Object
            || !target.TryGetProperty("box_2d", out JsonElement box)
            || box.ValueKind != JsonValueKind.Array
            || box.GetArrayLength() != 4)
        {
            return null;
        }

        double[] values = new double[4];
        int index = 0;
        foreach (JsonElement value in box.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            values[index++] = value.GetDouble();
        }

        // box_2d is y-first: ymin, xmin, ymax, xmax.
        int left = ToPixels(values[1], imageWidth);
        int top = ToPixels(values[0], imageHeight);
        int right = ToPixels(values[3], imageWidth);
        int bottom = ToPixels(values[2], imageHeight);

        if (right <= left || bottom <= top)
        {
            return null;
        }

        string label = target.TryGetProperty("label", out JsonElement l)
            && l.ValueKind == JsonValueKind.String
                ? l.GetString() ?? string.Empty
                : string.Empty;

        return new VisionTarget(label, new PhysicalScreenBounds(left, top, right - left, bottom - top));
    }

    /// <summary>Converts one normalised coordinate, clamped to the image.</summary>
    private static int ToPixels(double normalized, int extent)
    {
        double scaled = normalized / NormalizedScale * extent;
        return (int)Math.Round(Math.Clamp(scaled, 0, extent));
    }
}

/// <summary>The answer text and located elements from one guidance reply.</summary>
internal sealed record VisionTargetPayload(string Answer, IReadOnlyList<VisionTarget> Targets);
