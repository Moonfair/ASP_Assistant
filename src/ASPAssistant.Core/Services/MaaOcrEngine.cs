using System.Text.Json;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;

namespace ASPAssistant.Core.Services;

/// <summary>
/// OCR engine backed by MaaFramework / PaddleOCR.
/// Requires <c>det.onnx</c>, <c>rec.onnx</c>, and <c>keys.txt</c> to be present
/// under <c>{resourcePath}/model/ocr/</c>.
/// Unlike the Windows built-in OCR engine this implementation:
/// <list type="bullet">
///   <item>Has no system language-pack dependency.</item>
///   <item>Returns a bounding box for each individual matched text segment,
///         enabling precise per-item overlay marker placement.</item>
/// </list>
/// </summary>
public sealed class MaaOcrEngine : IOcrEngine, IDisposable
{
    private readonly MaaTasker _tasker;
    private readonly NullController _nullController;

    /// <param name="resourcePath">
    /// Directory that contains the <c>model/ocr/</c> folder with PaddleOCR ONNX files.
    /// Typically this is the application's <c>data/</c> directory.
    /// </param>
    public MaaOcrEngine(string resourcePath)
    {
        _nullController = new NullController();

        var controller = new MaaCustomController(_nullController);
        controller.LinkStart().Wait();

        var ocrModelPath = Path.Combine(resourcePath, "model", "ocr");
        var resource = new MaaResource();
        resource.AppendOcrModel(ocrModelPath).Wait();

        _tasker = new MaaTasker(controller, resource, DisposeOptions.All);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(
        byte[] pngBytes, int x, int y, int width, int height)
    {
        return await Task.Run(() => RecognizeRegion(pngBytes, x, y, width, height));
    }

    private IReadOnlyList<OcrTextResult> RecognizeRegion(
        byte[] pngBytes, int x, int y, int width, int height)
    {
        try
        {
            using var imgBuf = new MaaImageBuffer();
            if (!imgBuf.TrySetEncodedData(pngBytes))
                return [];

            var paramJson = $$"""{"roi":[{{x}},{{y}},{{width}},{{height}}]}""";

            var job = _tasker.AppendRecognition("OCR", paramJson, imgBuf);
            var status = job.Wait();
            if (status != MaaJobStatus.Succeeded)
                return [];

            _tasker.GetTaskDetail(job.Id, out _, out long[]? nodeIds, out _);
            if (nodeIds is not { Length: > 0 })
                return [];

            _tasker.GetNodeDetail(nodeIds[0], out _, out long recoId, out _, out _);
            _tasker.GetRecognitionDetail(recoId, out _, out _, out _,
                null, out string detailJson, null, null);

            return ParseOcrDetail(detailJson);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Parses the JSON returned by MaaFramework OCR recognition detail.
    /// Expected shape: <c>{"all":[{"text":"...","score":0.9,"box":[x,y,w,h]},...]}</c>
    /// </summary>
    private static IReadOnlyList<OcrTextResult> ParseOcrDetail(string detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(detailJson);
            if (!doc.RootElement.TryGetProperty("all", out var all))
                return [];

            var results = new List<OcrTextResult>();
            foreach (var item in all.EnumerateArray())
            {
                var text = item.GetProperty("text").GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(text))
                    continue;

                var box = item.GetProperty("box");
                int bx = box[0].GetInt32();
                int by = box[1].GetInt32();
                int bw = box[2].GetInt32();
                int bh = box[3].GetInt32();
                results.Add(new OcrTextResult(text, (bx, by, bw, bh)));
            }
            return results;
        }
        catch
        {
            return [];
        }
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
