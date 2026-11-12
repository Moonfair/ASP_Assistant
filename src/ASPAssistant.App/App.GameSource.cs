using System.IO;
using System.Windows;
using System.Windows.Threading;
using ASPAssistant.App.Windows;
using ASPAssistant.Core;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App;

public partial class App
{
    private readonly GameTargetSelection _gameTarget = new();
    private readonly EmulatorDiscovery _emulatorDiscovery = new();
    private readonly CancellationTokenSource _gameSourceShutdown = new();
    private DispatcherTimer? _gameSourceTimer;
    private bool _refreshingGameTarget;

    private void ConfigureGameSource()
    {
        var preference = _settingsManager!.LoadGameTarget();
        _gameTarget.Select(preference, preference.Id == GameTarget.WindowsId ? GameTargetSelection.FindWindowsClient() : null);
        _sidePanel!.SelectGameSourceRequested += SelectGameSource;
        UpdateGameSourceStatus();

        _gameSourceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _gameSourceTimer.Tick += async (_, _) => await RefreshGameSourceAsync();
        _gameSourceTimer.Start();
        _ = RefreshGameSourceAsync();
    }

    private void SelectGameSource()
    {
        var dialog = new GameSourceWindow(_emulatorDiscovery, _gameTarget.Preference) { Owner = _sidePanel };
        if (dialog.ShowDialog() != true || dialog.Preference == null) return;
        _overlay?.Hide();
        _gameTarget.Select(dialog.Preference, dialog.Target);
        try { _settingsManager!.SaveGameTarget(dialog.Preference); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Settings", "Could not save game source", ex);
            MessageBox.Show(_sidePanel!, "本次已切换，保存游戏来源失败。", "游戏来源", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateGameSourceStatus();
        _ = RefreshGameSourceAsync();
    }

    private async Task RefreshGameSourceAsync()
    {
        if (_refreshingGameTarget || _gameSourceShutdown.IsCancellationRequested) return;
        _refreshingGameTarget = true;
        var preference = _gameTarget.Preference;
        var token = _gameSourceShutdown.Token;

        try
        {
            GameTarget? target;
            switch (preference.Id)
            {
                case GameTarget.AutomaticId:
                    var active = _gameTarget.Current;
                    target = await Task.Run(() => _emulatorDiscovery.FindActiveGameAsync(active, token), token);
                    break;
                case GameTarget.WindowsId:
                    target = GameTargetSelection.FindWindowsClient();
                    break;
                case "adb":
                    target = _gameTarget.Current is { } current && current.HasLiveWindow()
                        ? current
                        : await Task.Run(() => GameTargetSelection.FindManualTarget(preference,
                            GameTargetSelection.ListWindows()), token);
                    break;
                default:
                    var instances = await Task.Run(() => _emulatorDiscovery.FindAsync(token), token);
                    target = instances.FirstOrDefault(t => string.Equals(t.Id, preference.Id, StringComparison.OrdinalIgnoreCase));
                    if (target == null && _gameTarget.Current is { } previous && previous.HasLiveWindow())
                        target = previous;
                    break;
            }
            if (!token.IsCancellationRequested && ReferenceEquals(preference, _gameTarget.Preference))
            {
                _gameTarget.SetCurrent(target);
                UpdateGameSourceStatus();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLogger.Error("Emulator", "Could not refresh game source", ex);
        }
        finally { _refreshingGameTarget = false; }
    }

    private void UpdateGameSourceStatus()
    {
        var target = _gameTarget.Current;
        var sourceName = target?.Kind switch
        {
            GameTargetKind.Windows => "Windows 客户端",
            GameTargetKind.MuMu => $"MuMu · {target.Name}（实例 {target.Instance}）",
            GameTargetKind.LdPlayer => $"雷电 · {target.Name}（实例 {target.Instance}）",
            GameTargetKind.Adb => $"ADB · {target.Name}",
            _ => _gameTarget.Preference.Id switch
            {
                GameTarget.AutomaticId => "自动检测",
                GameTarget.WindowsId => "Windows 客户端",
                "adb" => "手动绑定 ADB",
                _ => "已保存的模拟器"
            }
        };
        if (_gameTarget.Preference.Id == GameTarget.AutomaticId && target != null)
            sourceName = $"自动检测 · {sourceName}";
        _sidePanel?.SetGameSourceStatus(target == null ? $"{sourceName} · 等待窗口" : sourceName);
    }

    private void StopGameSource()
    {
        _gameSourceTimer?.Stop();
        _gameSourceShutdown.Cancel();
    }
}
