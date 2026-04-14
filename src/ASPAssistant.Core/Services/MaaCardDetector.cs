using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Detects operator card boundaries in the shop area using MaaFramework's
/// TemplateMatch algorithm against <c>op_card.png</c>.
///
/// A single recognition call with <c>count: 8</c> is made; the returned
/// <c>detailJson</c> <c>"all"</c> array contains every match above threshold,
/// giving all card boundaries at once without per-operator iteration.
///
/// Template is loaded from the MaaFramework resource bundle at
/// <c>{dataDir}/maa_resource/image/op_card.png</c>, copied there by the build.
/// The template was captured at 1280×720; when the actual game runs at a
/// different resolution the template is scaled proportionally before matching.
/// </summary>
public sealed class MaaCardDetector : ICardDetector, IDisposable
{
    /// <summary>Resolution at which <c>op_card.png</c> was captured.</summary>
    private const int TemplateBaseWidth  = 1280;
    private const int TemplateBaseHeight = 720;

    /// <summary>
    /// Minimum similarity score (0–1) for a card template match to be accepted.
    /// </summary>
    public double Threshold { get; init; } = 0.1;

    /// <summary>
    /// Maximum number of card instances to return per scan.
    /// The Garrison Protocol shop shows at most 8 operator slots.
    /// </summary>
    public int MaxCards { get; init; } = 8;

    private readonly MaaTasker _tasker;
    private readonly NullController _nullController;
    private readonly string _imageDir;

    // Keyed by (imgWidth, imgHeight) → scaled template filename (no path).
    private readonly ConcurrentDictionary<(int W, int H), string> _scaledTemplateCache = new();

    /// <param name="dataDir">
    /// The application's <c>data/</c> directory.  Must contain
    /// <c>maa_resource/image/op_card.png</c> (copied at build time from
    /// <c>data/template/op_card.png</c>).
    /// </param>
    public MaaCardDetector(string dataDir)
    {
        _nullController = new NullController();

        var controller = new MaaCustomController(_nullController);
        controller.LinkStart().Wait();

        var maaResourceDir = Path.Combine(dataDir, "maa_resource");
        _imageDir = Path.Combine(maaResourceDir, "image");

        var resource = new MaaResource();
        resource.AppendBundle(maaResourceDir).Wait();

        _tasker = new MaaTasker(controller, resource, DisposeOptions.All);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(int X, int Y, int W, int H)>> DetectCardsAsync(
        byte[] pngBytes, int roiX, int roiY, int roiWidth, int roiHeight,
        int imgWidth, int imgHeight)
    {
        return await Task.Run(() => DetectCards(pngBytes, roiX, roiY, roiWidth, roiHeight, imgWidth, imgHeight));
    }

    private IReadOnlyList<(int X, int Y, int W, int H)> DetectCards(
        byte[] pngBytes, int roiX, int roiY, int roiWidth, int roiHeight,
        int imgWidth, int imgHeight)
    {
        try
        {
            using var imgBuf = new MaaImageBuffer();
            if (!imgBuf.TrySetEncodedData(pngBytes))
                return [];

            var templateName = GetScaledTemplateName(imgWidth, imgHeight);

            // order_by "Horizontal" sorts results left-to-right, matching shop slot order.
            var threshold = Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var paramJson = $$"""{"roi":[{{roiX}},{{roiY}},{{roiWidth}},{{roiHeight}}],"template":"{{templateName}}","threshold":{{threshold}},"order_by":"Horizontal","green_mask":true,"method":10001}""";

            var job = _tasker.AppendRecognition("TemplateMatch", paramJson, imgBuf);
            if (job.Wait() != MaaJobStatus.Succeeded)
                return [];

            _tasker.GetTaskDetail(job.Id, out _, out long[]? nodeIds, out _);
            if (nodeIds is not { Length: > 0 })
                return [];

            _tasker.GetNodeDetail(nodeIds[0], out _, out long recoId, out _, out _);

            // hitBox only holds the single best match; parse detailJson["all"] for all cards.
            _tasker.GetRecognitionDetail(recoId, out _, out _, out bool wasHit,
                null, out string detailJson, null, null);

            if (!wasHit || string.IsNullOrWhiteSpace(detailJson))
                return [];

            return ParseAllBoxes(detailJson);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Returns the filename (relative to the MAA image bundle) of a version of
    /// <c>op_card.png</c> scaled to match <paramref name="imgWidth"/>×<paramref name="imgHeight"/>.
    /// The scaled file is generated on first use and then cached on disk and in memory
    /// so subsequent calls at the same resolution are free.
    /// </summary>
    private string GetScaledTemplateName(int imgWidth, int imgHeight)
    {
        if (imgWidth == TemplateBaseWidth && imgHeight == TemplateBaseHeight)
            return "op_card.png";

        return _scaledTemplateCache.GetOrAdd((imgWidth, imgHeight), key =>
        {
            double scaleX = key.W / (double)TemplateBaseWidth;
            double scaleY = key.H / (double)TemplateBaseHeight;

            var srcPath    = Path.Combine(_imageDir, "op_card.png");
            var scaledName = $"op_card_{key.W}x{key.H}.png";
            var dstPath    = Path.Combine(_imageDir, scaledName);

            if (!File.Exists(dstPath))
            {
                using var src = new Bitmap(srcPath);
                int newW = Math.Max(1, (int)Math.Round(src.Width  * scaleX));
                int newH = Math.Max(1, (int)Math.Round(src.Height * scaleY));

                using var scaled = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(scaled);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode     = SmoothingMode.HighQuality;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, newW, newH);
                scaled.Save(dstPath, ImageFormat.Png);

                AppLogger.Info("CardDetector",
                    $"Scaled template saved: {scaledName} ({src.Width}x{src.Height} → {newW}x{newH}, scale={scaleX:F3}x{scaleY:F3})");
            }

            return scaledName;
        });
    }

    /// <summary>
    /// Parses the TemplateMatch detail JSON, extracting the <c>"all"</c> array of
    /// matched bounding boxes.
    /// Expected shape: <c>{"all":[{"score":0.9,"box":[x,y,w,h]},...],"best":{...}}</c>
    /// </summary>
    private static IReadOnlyList<(int X, int Y, int W, int H)> ParseAllBoxes(string detailJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(detailJson);

            // Prefer the "all" array (multiple matches).
            if (doc.RootElement.TryGetProperty("all", out var all))
            {
                var boxes = new List<(int, int, int, int)>();
                foreach (var item in all.EnumerateArray())
                {
                    if (!item.TryGetProperty("box", out var boxEl))
                        continue;
                    var arr = boxEl.EnumerateArray().ToArray();
                    if (arr.Length < 4)
                        continue;
                    boxes.Add((arr[0].GetInt32(), arr[1].GetInt32(),
                               arr[2].GetInt32(), arr[3].GetInt32()));
                }
                if (boxes.Count > 0)
                    return boxes;
            }

            // Fallback: use "best" (single best match).
            if (doc.RootElement.TryGetProperty("best", out var best) &&
                best.TryGetProperty("box", out var bestBox))
            {
                var arr = bestBox.EnumerateArray().ToArray();
                if (arr.Length >= 4)
                    return [(arr[0].GetInt32(), arr[1].GetInt32(),
                             arr[2].GetInt32(), arr[3].GetInt32())];
            }
        }
        catch { }

        return [];
    }

    public void Dispose()
    {
        _tasker.Dispose();
        _nullController.Dispose();
    }

    private sealed class NullController : IMaaCustomController
    {
        public string Name { get; set; } = "NullController";

        public bool Connect() => true;
        public bool Connected() => true;
        public bool RequestUuid(IMaaStringBuffer uuidBuffer) => true;
        public ControllerFeatures GetFeatures() => ControllerFeatures.None;
        public bool GetInfo(IMaaStringBuffer infoBuffer) => true;
        public bool Inactive() => true;

        public bool Screencap(IMaaImageBuffer imageBuffer) => false;
        public bool Click(int x, int y) => false;
        public bool Swipe(int x1, int y1, int x2, int y2, int duration) => false;
        public bool TouchDown(int contact, int x, int y, int pressure) => false;
        public bool TouchMove(int contact, int x, int y, int pressure) => false;
        public bool TouchUp(int contact) => false;
        public bool ClickKey(int key) => false;
        public bool KeyDown(int key) => false;
        public bool KeyUp(int key) => false;
        public bool InputText(string text) => false;
        public bool StartApp(string intent) => false;
        public bool StopApp(string intent) => false;
        public bool Scroll(int x, int y) => false;

        public void Dispose() { }
    }
}
