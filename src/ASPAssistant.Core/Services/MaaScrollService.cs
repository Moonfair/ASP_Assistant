using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Executes Win32 wheel scrolling through Maa action pipeline with an explicit target point.
/// </summary>
public sealed class MaaScrollService
{
    private readonly string _maaResourceDir;

    public MaaScrollService(string dataDir)
    {
        _maaResourceDir = Path.Combine(dataDir, "maa_resource");
    }

    /// <summary>
    /// Scrolls at the center of the target game window client area by Maa "Scroll" action.
    /// </summary>
    public bool TryScrollAtWindowCenter(IntPtr hwnd, int dy)
    {
        if (hwnd == IntPtr.Zero || !Interop.User32.IsWindow(hwnd))
            return false;

        var clientRect = Interop.User32.GetClientRectScreen(hwnd);
        if (clientRect == null)
            return false;

        int cx = clientRect.Value.Left + clientRect.Value.Width / 2;
        int cy = clientRect.Value.Top + clientRect.Value.Height / 2;

        try
        {
            var screencapMethod = (Win32ScreencapMethod)2;
            var mouseMethod = Win32InputMethod.Seize;
            var keyboardMethod = Win32InputMethod.Seize;

            var winInfo = new DesktopWindowInfo(
                hwnd,
                "Arknights",
                string.Empty,
                screencapMethod,
                mouseMethod,
                keyboardMethod);

            using var controller = new MaaWin32Controller(winInfo, LinkOption.Start, CheckStatusOption.None);
            using var resource = new MaaResource();
            resource.AppendBundle(_maaResourceDir).Wait();

            using var tasker = new MaaTasker(controller, resource, DisposeOptions.All);
            using var box = new MaaRectBuffer();
            box.TrySetValues(cx, cy, 1, 1);

            var warmup = controller.Screencap();
            warmup.Wait();

            // Split into wheel ticks (120 per notch) because some runtimes effectively
            // apply only one notch per Scroll action regardless of large dy magnitude.
            int direction = dy >= 0 ? 1 : -1;
            int total = Math.Abs(dy);
            int steps = Math.Max(1, (int)Math.Ceiling(total / 120.0));
            int stepDy = direction * 120;
            int succeededSteps = 0;
            MaaJobStatus status = MaaJobStatus.Failed;
            for (int i = 0; i < steps; i++)
            {
                var actionParam = $$"""{"point":[{{cx}},{{cy}}],"dx":0,"dy":{{stepDy}}}""";
                var job = tasker.AppendAction("Scroll", actionParam, box, "{}");
                status = job.Wait();

                if (status != MaaJobStatus.Succeeded)
                    break;

                succeededSteps++;
            }
            return succeededSteps == steps;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("MaaScroll", $"Maa scroll failed: {ex.Message}");
            return false;
        }
    }
}
