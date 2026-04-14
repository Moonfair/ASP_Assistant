namespace ASPAssistant.Core.Services;

/// <summary>
/// Contract for image-based operator card boundary detection.
/// Finds all operator card frames visible in a region of the screenshot by
/// matching against a card template image, returning their pixel bounding boxes.
/// </summary>
public interface ICardDetector
{
    /// <summary>
    /// Searches <paramref name="pngBytes"/> inside the given ROI for all
    /// operator card instances.
    /// Coordinates are pixels relative to the full image.
    /// <paramref name="imgWidth"/> and <paramref name="imgHeight"/> are the full
    /// image dimensions; they are used to scale the card template (captured at
    /// 1280×720) to match the actual game resolution before matching.
    /// Returns one bounding box per detected card.
    /// </summary>
    Task<IReadOnlyList<(int X, int Y, int W, int H)>> DetectCardsAsync(
        byte[] pngBytes, int roiX, int roiY, int roiWidth, int roiHeight,
        int imgWidth, int imgHeight);
}
