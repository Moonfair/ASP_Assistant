using System.Timers;
using ASPAssistant.Core.Interop;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class WindowTrackerService : IDisposable
{
    private IntPtr _gameWindowHandle;
    private readonly Timer _pollTimer;
    private readonly Func<int> _getScreenWidth;

    public event Action<RECT>? GameWindowMoved;
    public event Action? GameWindowLost;
    public event Action<bool>? GameWindowFocusChanged;
    public event Action<RECT>? GameWindowPolled;

    public RECT? CurrentGameRect { get; private set; }
    public bool IsGameFocused { get; private set; }
    public bool IsGameFound => _gameWindowHandle != IntPtr.Zero
                                && User32.IsWindow(_gameWindowHandle);
    public bool ShouldAttachInside { get; private set; }

    public WindowTrackerService(Func<int>? getScreenWidth = null, int pollIntervalMs = 100)
    {
        _getScreenWidth = getScreenWidth ?? (() => 1920);
        _pollTimer = new Timer(pollIntervalMs);
        _pollTimer.Elapsed += OnPollTick;
    }

    public void Start()
    {
        _gameWindowHandle = User32.FindArknightsWindow();
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    private void OnPollTick(object? sender, ElapsedEventArgs e)
    {
        if (!IsGameFound)
        {
            _gameWindowHandle = User32.FindArknightsWindow();
            if (_gameWindowHandle == IntPtr.Zero)
            {
                if (CurrentGameRect != null)
                {
                    CurrentGameRect = null;
                    GameWindowLost?.Invoke();
                }
                return;
            }
        }

        var foreground = User32.GetForegroundWindow();
        var wasFocused = IsGameFocused;
        IsGameFocused = foreground == _gameWindowHandle;
        if (wasFocused != IsGameFocused)
            GameWindowFocusChanged?.Invoke(IsGameFocused);

        if (!User32.GetWindowRect(_gameWindowHandle, out var windowRect))
        {
            CurrentGameRect = null;
            GameWindowLost?.Invoke();
            _gameWindowHandle = IntPtr.Zero;
            return;
        }

        var screenWidth = GetPrimaryScreenWidth();
        var rightSpace = screenWidth - windowRect.Right;
        var shouldAttachInside = rightSpace < 320 || IsFullscreen(windowRect, screenWidth);

        var changed = IsGameWindowChanged(CurrentGameRect, ShouldAttachInside,
            windowRect, shouldAttachInside);
        ShouldAttachInside = shouldAttachInside;
        CurrentGameRect = windowRect;

        if (changed)
            GameWindowMoved?.Invoke(windowRect);
        else
            GameWindowPolled?.Invoke(windowRect);
    }

    private static bool IsFullscreen(RECT rect, int screenWidth)
    {
        return rect.Left <= 0 && rect.Width >= screenWidth - 10;
    }

    private int GetPrimaryScreenWidth()
    {
        return _getScreenWidth();
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
