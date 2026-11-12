using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class WindowTrackerServiceTests
{
    private static RECT R(int l, int t, int r, int b) =>
        new() { Left = l, Top = t, Right = r, Bottom = b };

    // 首帧：previous 为 null，视为"变化"
    [Fact]
    public void IsGameWindowChanged_WhenPreviousIsNull_ReturnsTrue()
    {
        WindowTrackerService.IsGameWindowChanged(null, R(0, 0, 1920, 1080))
            .Should().BeTrue();
    }

    // RECT 无变化 → 稳定心跳
    [Fact]
    public void IsGameWindowChanged_WhenRectUnchanged_ReturnsFalse()
    {
        var rect = R(100, 50, 1820, 1080);
        WindowTrackerService.IsGameWindowChanged(rect, rect)
            .Should().BeFalse();
    }

    // RECT 变化 → 真实移动
    [Fact]
    public void IsGameWindowChanged_WhenRectChanged_ReturnsTrue()
    {
        WindowTrackerService.IsGameWindowChanged(R(0, 0, 100, 100), R(10, 0, 110, 100))
            .Should().BeTrue();
    }
}
