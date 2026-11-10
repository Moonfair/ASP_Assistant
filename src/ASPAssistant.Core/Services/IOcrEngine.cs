namespace ASPAssistant.Core.Services;

/// <summary>
/// Represents a single OCR recognition result: the recognized text and its
/// pixel bounding box relative to the original (un-cropped) screenshot.
/// </summary>
public record OcrTextResult(string Text, (int X, int Y, int W, int H) Box);

/// <summary>
/// Contract for OCR engines used by <see cref="OcrScannerService"/>.
/// Returns per-segment results with individual bounding boxes instead of a
/// flat string, enabling precise per-item matching and overlay placement.
/// </summary>
public interface IOcrEngine
{
    /// <summary>
    /// Runs OCR on the specified sub-region of <paramref name="pngBytes"/>.
    /// Coordinates are pixels relative to the full image.
    /// Returns all recognized text segments with their absolute pixel boxes.
    /// </summary>
    Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(
        byte[] pngBytes, int x, int y, int width, int height);
}
