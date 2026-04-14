using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;

namespace ASPAssistant.Core.Services;


/// <summary>
/// Detects banned operators in the pre-match ban screen by matching their skin avatar
/// portrait templates against a screenshot using MaaFramework's FeatureMatch.
///
/// Templates are skin avatar images downloaded from PRTS and stored at:
///   {dataDir}/maa_resource/image/skin_avatars/{avatarId}.png
/// </summary>
public sealed class MaaBanIconMatcher : IDisposable
{

    /// <summary>
    /// Minimum number of matched feature points required for a FeatureMatch hit in
    /// <see cref="FindBestInSlotAsync"/>. Higher values reduce false positives but may
    /// miss heavily-scaled or partially-visible portraits.
    /// </summary>
    public uint OperatorFeatureCount { get; init; } = 4;//16

    /// <summary>
    /// Feature detector used by <see cref="FindBestInSlotAsync"/>.
    /// BRISK is the recommended balance of speed and scale-invariance for ban-screen matching.
    /// Options (fast→slow): ORB · BRISK · AKAZE · SIFT · KAZE
    /// </summary>
    public string FeatureDetector { get; init; } = "SIFT";

    /// <summary>
    /// KNN distance ratio for feature point matching [0–1.0]. Larger = more lenient (easier to
    /// connect). Default 0.6 per MaaFramework docs.
    /// </summary>
    public double OperatorRatio { get; init; } = 0.6;//0.55

    /// <summary>
    /// ROI for ban-screen operator matching, expressed as fractions of the screenshot dimensions.
    /// Corresponds to the yellow operator grid area on the "确认本局信息" screen.
    /// </summary>
    public double BanRoiX { get; init; } = 0.216;
    public double BanRoiY { get; init; } = 0.222;
    public double BanRoiW { get; init; } = 0.783;
    public double BanRoiH { get; init; } = 0.775;

    /// <summary>The application's <c>data/</c> directory passed at construction time.</summary>
    public string DataDir => _dataDir;

    private readonly string _dataDir;
    private readonly MaaTasker _tasker;
    private readonly NullController _nullController;

    /// <param name="dataDir">
    /// The application's <c>data/</c> directory at runtime. Must contain
    /// <c>maa_resource/image/skin_avatars/*.png</c> (copied at build time from
    /// <c>data/icons/skin_avatars/</c> in the repository).
    /// </param>
    public MaaBanIconMatcher(string dataDir)
    {
        _dataDir = dataDir;
        _nullController = new NullController();

        var controller = new MaaCustomController(_nullController);
        controller.LinkStart().Wait();

        var resourcePath = Path.Combine(dataDir, "maa_resource");
        AppLogger.Info("MaaBanIconMatcher",
            $"Initialising — dataDir={dataDir}, resourcePath={resourcePath}, " +
            $"detector={FeatureDetector}, count={OperatorFeatureCount}, ratio={OperatorRatio}, " +
            $"roi=({BanRoiX:F3},{BanRoiY:F3},{BanRoiW:F3},{BanRoiH:F3})");

        var resource = new MaaResource();
        resource.AppendBundle(resourcePath).Wait();

        _tasker = new MaaTasker(controller, resource, DisposeOptions.All);
    }


    /// <summary>
    /// Scans the ban-screen ROI of the screenshot using FeatureMatch with all skin templates for
    /// the candidate operator passed as a batch array. Returns a hit when any template matches
    /// (matched feature points ≥ <see cref="OperatorFeatureCount"/>).
    /// Returns <c>null</c> on internal error.
    /// </summary>
    public Task<(string Name, bool Hit, System.Drawing.Rectangle HitBox)?> FindBestInSlotAsync(
        byte[] screenshotPng,
        (string Name, IReadOnlyList<string> TemplateNames) candidate,
        int imgWidth,
        int imgHeight)
        => Task.Run(() => FindBestInSlot(screenshotPng, candidate, imgWidth, imgHeight));

    /// <summary>
    /// Maximum image width used for feature matching. If the loaded screenshot is wider
    /// than this, it is downscaled via <see cref="MaaImageBuffer.TryResize"/> before
    /// running recognition, reducing feature extraction cost proportionally.
    /// </summary>
    public int MaxMatchWidth { get; init; } = 1920;

    private (string Name, bool Hit, System.Drawing.Rectangle HitBox)? FindBestInSlot(
        byte[] screenshotPng, (string Name, IReadOnlyList<string> TemplateNames) candidate,
        int imgWidth, int imgHeight)
    {
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        using var imgBuf = new MaaImageBuffer();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!imgBuf.TrySetEncodedData(screenshotPng))
            return null;
        var decodeMs = sw.ElapsedMilliseconds;

        // Downscale inside MAA if the captured image is still larger than MaxMatchWidth.
        // This is a safety net that also fires when ScreenCaptureService pre-scaling is
        // bypassed (e.g. future callers passing raw bytes at native resolution).
        long resizeMs = 0;
        if (imgWidth > MaxMatchWidth)
        {
            sw.Restart();
            float scale = (float)MaxMatchWidth / imgWidth;
            int scaledH = (int)(imgHeight * scale);
            imgBuf.TryResize(MaxMatchWidth, scaledH);
            resizeMs = sw.ElapsedMilliseconds;
            imgWidth  = MaxMatchWidth;
            imgHeight = scaledH;
        }

        int roiX = (int)(BanRoiX * imgWidth);
        int roiY = (int)(BanRoiY * imgHeight);
        int roiW = (int)(BanRoiW * imgWidth);
        int roiH = (int)(BanRoiH * imgHeight);

        var templateArray = "[" + string.Join(",", candidate.TemplateNames.Select(t => $"\"{t}\"")) + "]";
        var ratio = OperatorRatio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var paramJson =
            $$"""{"roi":[{{roiX}},{{roiY}},{{roiW}},{{roiH}}],"template":{{templateArray}},"count":{{OperatorFeatureCount}},"ratio":{{ratio}},"detector":"{{FeatureDetector}}"}""";
        try
        {
            sw.Restart();
            var job = _tasker.AppendRecognition("FeatureMatch", paramJson, imgBuf);
            var status = job.Wait();
            var featureMs = sw.ElapsedMilliseconds;

            if (status != MaaJobStatus.Succeeded)
            {
                AppLogger.Warn("MaaBanIconMatcher",
                    $"[{candidate.Name}] FeatureMatch job failed — decode={decodeMs}ms resize={resizeMs}ms feature={featureMs}ms total={totalSw.ElapsedMilliseconds}ms");
                return null;
            }

            sw.Restart();
            _tasker.GetTaskDetail(job.Id, out _, out long[]? nodeIds, out _);
            if (nodeIds is not { Length: > 0 })
                return null;

            _tasker.GetNodeDetail(nodeIds[0], out _, out long recoId, out _, out _);

            using var hitBoxBuf = new MaaFramework.Binding.Buffers.MaaRectBuffer();
            _tasker.GetRecognitionDetail(recoId, out _, out _, out bool wasHit,
                hitBoxBuf, out string detailJson, null, null);
            var detailMs = sw.ElapsedMilliseconds;

            AppLogger.Info("MaaBanIconMatcher",
                $"[{candidate.Name}] decode={decodeMs}ms resize={resizeMs}ms feature={featureMs}ms detail={detailMs}ms total={totalSw.ElapsedMilliseconds}ms hit={wasHit}");

            return (candidate.Name, wasHit, new System.Drawing.Rectangle(
                hitBoxBuf.X, hitBoxBuf.Y, hitBoxBuf.Width, hitBoxBuf.Height));
        }
        catch (Exception ex)
        {
            AppLogger.Error("MaaBanIconMatcher", $"FindBestInSlot failed for '{candidate.Name}'", ex);
        }

        return null;
    }

    public void Dispose()
    {
        _tasker.Dispose();
        _nullController.Dispose();
    }

    // ?? Null controller ???????????????????????????????????????????????????????
    // MAA requires a controller even for screenshot-only recognition. This
    // implementation satisfies the interface without performing any real I/O.
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
