using System.Windows;
using System.Windows.Interop;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Tests.Views;

public class OverlayMatchStrokeTests
{
    [Fact]
    public void FirstPressOnOverlayIsDeliveredWithoutTakingForegroundFromGameOrSidebar()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var overlay = new OverlayWindow(new SettingsManager(Path.GetTempPath()));
            try
            {
                var hwnd = new WindowInteropHelper(overlay).EnsureHandle();
                const int wmMouseActivate = 0x0021, htClient = 1, wmLeftButtonDown = 0x0201, maNoActivate = 3;
                var response = User32.SendMessage(hwnd, wmMouseActivate, hwnd, (wmLeftButtonDown << 16) | htClient);
                Assert.Equal((nint)maNoActivate, response);
            }
            finally { overlay.Close(); }
        });
    }

    [Fact]
    public void CapturedDragUsesTheViewportAtEachSampleAndClampsOutsidePoints()
    {
        var path = new MatchStrokePath();
        path.Add(new Point(20, 30), new Size(80, 60));
        path.Add(new Point(40, 60), new Size(160, 120));
        path.Add(new Point(-1, 130), new Size(160, 120));

        var points = path.Finish();
        Assert.Collection(points,
            first => Assert.Equal((0.25, 0.5), (first.X, first.Y)),
            last => Assert.Equal((0.0, 1.0), (last.X, last.Y)));
    }

    [Fact]
    public void TapSurvivesAsAZeroLengthStrokeAndEmptyViewportIsIgnored()
    {
        var path = new MatchStrokePath();
        path.Add(new Point(0, 0), new Size(0, 0));
        Assert.Empty(path.Finish());
        path.Add(new Point(10, 30), new Size(100, 100));

        var points = path.Finish();
        Assert.Equal(2, points.Length);
        Assert.All(points, p => Assert.Equal((0.1, 0.3), (p.X, p.Y)));
    }

    [Fact]
    public void LongDragPreservesItsEndpointsAndBendsWithinTheWireLimit()
    {
        var path = new MatchStrokePath();
        var size = new Size(2000, 1000);
        for (int i = 0; i <= 2000; i++)
            path.Add(new Point(i, i <= 1000 ? i : 2000 - i), size);

        var points = path.Finish();
        Assert.InRange(points.Length, 3, 512);
        Assert.Equal((0.0, 0.0), (points[0].X, points[0].Y));
        Assert.Contains(points, p => p.X == 0.5 && p.Y == 1);
        Assert.Equal((1.0, 0.0), (points[^1].X, points[^1].Y));
    }
}
