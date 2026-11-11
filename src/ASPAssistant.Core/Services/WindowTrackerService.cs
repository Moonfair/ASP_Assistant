using System.Timers;
using ASPAssistant.Core.Interop;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class WindowTrackerService : IDisposable
{
    private readonly GameTargetSelection _selection;
    private readonly Timer _pollTimer;
    private int _polling;

    public event Action<RECT>? GameWindowMoved;
    public event Action? GameWindowLost;
    public event Action<bool>? GameWindowFocusChanged;
    public event Action<RECT>? GameWindowPolled;

    public RECT? CurrentGameRect { get; private set; }
    public GameTarget? CurrentTarget { get; private set; }
    public bool IsGameFocused { get; private set; }
    public bool ShouldAttachInside { get; private set; }

    public WindowTrackerService(GameTargetSelection selection, int pollIntervalMs = 100)
    {
        _selection = selection;
        _pollTimer = new Timer(pollIntervalMs);
        _pollTimer.Elapsed += OnPollTick;
    }

    public void Start()
    {
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    private void OnPollTick(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try { Poll(); }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private void Poll()
    {
        var target = _selection.Current;
        var window = target?.Window ?? 0;
        var wasFocused = IsGameFocused;
        IsGameFocused = window != 0 && User32.GetRootWindow(User32.GetForegroundWindow()) == window;
        if (wasFocused != IsGameFocused)
            GameWindowFocusChanged?.Invoke(IsGameFocused);

        bool changedTarget = !ReferenceEquals(CurrentTarget, target);
        CurrentTarget = target;
        if (target == null || !target.HasLiveWindow() || User32.IsIconic(window) || !User32.IsWindowVisible(window) ||
            !User32.GetWindowRect(window, out var windowRect))
        {
            if (CurrentGameRect != null)
            {
                CurrentGameRect = null;
                GameWindowLost?.Invoke();
            }
            return;
        }

        // Use the monitor that contains the game window's right edge so that
        // multi-monitor setups resolve correctly regardless of primary screen.
        var monitorRect = User32.GetMonitorRect(windowRect.Right - 1, windowRect.Top);
        var rightSpace = monitorRect.Right - windowRect.Right;
        var shouldAttachInside = rightSpace < 320 || IsFullscreen(windowRect, monitorRect);

        var changed = changedTarget || IsGameWindowChanged(CurrentGameRect, ShouldAttachInside,
            windowRect, shouldAttachInside);
        ShouldAttachInside = shouldAttachInside;
        CurrentGameRect = windowRect;

        if (changed)
            GameWindowMoved?.Invoke(windowRect);
        else
            GameWindowPolled?.Invoke(windowRect);
    }

    private static bool IsFullscreen(RECT rect, RECT monitorRect)
    {
        return rect.Left <= monitorRect.Left && rect.Width >= monitorRect.Width - 10;
    }

    public static bool IsGameWindowChanged(RECT? previous, bool previousAttachInside,
        RECT current, bool currentAttachInside)
    {
        if (previous is null) return true;
        var p = previous.Value;
        return p.Left != current.Left ||
               p.Top != current.Top ||
               p.Right != current.Right ||
               p.Bottom != current.Bottom ||
               previousAttachInside != currentAttachInside;
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
    }
}
