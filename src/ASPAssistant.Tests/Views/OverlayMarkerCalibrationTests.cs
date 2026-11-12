using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class OverlayMarkerCalibrationTests
{
    [Fact]
    public void SavedOffsetAppliesToNewAndExistingMarkersAcrossViewportSizes()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var dir = Path.Combine(Path.GetTempPath(), $"asp_marker_{Guid.NewGuid():N}");
            var settings = new SettingsManager(dir);
            settings.SaveMarkerOffset(new MarkerOffset(0.05, -0.1));
            var overlay = new OverlayWindow(settings);
            try
            {
                var canvas = (Canvas)overlay.Content;
                ResizeCanvas(canvas, 640, 360);
                var first = new ShopItem { Id = "first", IsTracked = true, OcrRegion = (0.2, 0.8, 0.1, 0.1) };
                overlay.UpdateShopItems([first]);
                overlay.UpdateBanMarkers([new Rectangle(200, 200, 20, 20)], 1000, 1000);
                var shopMarker = (FrameworkElement)canvas.Children[0];
                var banMarker = (FrameworkElement)canvas.Children[1];
                AssertPosition(shopMarker, 195, 254);
                AssertPosition(banMarker, 160, 36);

                ResizeCanvas(canvas, 1280, 720);
                AssertPosition(shopMarker, 419, 506);
                AssertPosition(banMarker, 320, 72);

                var second = new ShopItem { Id = "second", IsTracked = true, OcrRegion = (0.5, 0.8, 0.1, 0.1) };
                overlay.UpdateShopItems([first, second]);
                var newShopMarker = (FrameworkElement)canvas.Children[2];
                AssertPosition(newShopMarker, 803, 506);

                overlay.ResetMarkerOffset();
                AssertPosition(shopMarker, 355, 578);
                AssertPosition(banMarker, 256, 144);
                AssertPosition(newShopMarker, 739, 578);
                Assert.Equal(default, new SettingsManager(dir).LoadMarkerOffset());
            }
            finally
            {
                overlay.Close();
                Directory.Delete(dir, recursive: true);
            }
        });
    }

    [Fact]
    public void CalibrationModeTemporarilyEnablesMouseInput()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var settings = new SettingsManager(Path.Combine(Path.GetTempPath(), $"asp_marker_{Guid.NewGuid():N}"));
            var overlay = new OverlayWindow(settings);
            try
            {
                var hwnd = new WindowInteropHelper(overlay).EnsureHandle();
                bool IsClickThrough() =>
                    (User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE) & User32.WS_EX_TRANSPARENT) != 0;

                Assert.True(IsClickThrough());
                overlay.SetMarkerCalibration(true);
                Assert.False(IsClickThrough());
                overlay.SetMarkerCalibration(false);
                Assert.True(IsClickThrough());
            }
            finally { overlay.Close(); }
        });
    }

    private static void ResizeCanvas(Canvas canvas, double width, double height)
    {
        canvas.Measure(new System.Windows.Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();
    }

    private static void AssertPosition(FrameworkElement marker, double x, double y)
    {
        Assert.Equal(x, Canvas.GetLeft(marker), precision: 6);
        Assert.Equal(y, Canvas.GetTop(marker), precision: 6);
    }
}
