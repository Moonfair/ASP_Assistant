using System.Timers;
using ASPAssistant.Core.Interop;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class WindowTrackerService : IDisposable
{
    private IntPtr _gameWindowHandle;
    private readonly Timer _pollTimer;

    public event Action<RECT>? GameWindowMoved;
    public event Action? GameWindowLost;
    public event Action<bool>? GameWindowFocusChanged;

    public RECT? CurrentGameRect { get; private set; }
    public bool IsGameFocused { get; private set; }
    public bool IsGameFound => _gameWindowHandle != IntPtr.Zero
                                && User32.IsWindow(_gameWindowHandle);
    public bool ShouldAttachInside { get; private set; }

    public WindowTrackerService(int pollIntervalMs = 100)
    {
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
        ShouldAttachInside = rightSpace < 320 || IsFullscreen(windowRect, screenWidth);

        CurrentGameRect = windowRect;
        GameWindowMoved?.Invoke(windowRect);
    }

    private static bool IsFullscreen(RECT rect, int screenWidth)
    {
        return rect.Left <= 0 && rect.Width >= screenWidth - 10;
    }

    private static int GetPrimaryScreenWidth()
    {
        return 1920; // Will be replaced by actual SystemParameters in App layer
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
    }
}
