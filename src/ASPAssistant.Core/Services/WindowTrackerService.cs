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

        var changed = changedTarget || IsGameWindowChanged(CurrentGameRect, windowRect);
        CurrentGameRect = windowRect;

        if (changed)
            GameWindowMoved?.Invoke(windowRect);
        else
            GameWindowPolled?.Invoke(windowRect);
    }

    public static bool IsGameWindowChanged(RECT? previous, RECT current)
    {
        if (previous is null) return true;
        var p = previous.Value;
        return p.Left != current.Left ||
               p.Top != current.Top ||
               p.Right != current.Right ||
               p.Bottom != current.Bottom;
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
    }
}
