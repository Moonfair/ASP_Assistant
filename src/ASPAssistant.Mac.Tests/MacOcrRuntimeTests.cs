using ASPAssistant.Core.Services;

namespace ASPAssistant.Mac.Tests;

public class MacOcrRuntimeTests
{
    [Fact]
    public async Task MaaOcrLoadsMacRuntimeAndModel()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        Assert.True(File.Exists(Path.Combine(data, "model", "ocr", "rec.onnx")));
        using var engine = new MaaOcrEngine(data);
        var icon = Directory.GetFiles(Path.Combine(data, "icons", "operators"), "*.png").First();
        var bytes = await File.ReadAllBytesAsync(icon);
        var result = await engine.RecognizeRegionAsync(bytes, 0, 0, 16, 16);
        Assert.NotNull(result);
    }
}
