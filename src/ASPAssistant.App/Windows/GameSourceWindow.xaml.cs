using System.IO;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;
using Microsoft.Win32;

namespace ASPAssistant.App.Windows;

public partial class GameSourceWindow : Window
{
    private const string ManualId = "adb";
    private readonly EmulatorDiscovery _discovery;
    private readonly GameTargetPreference _saved;
    private readonly CancellationTokenSource _closed = new();

    public GameTargetPreference? Preference { get; private set; }
    public GameTarget? Target { get; private set; }

    public GameSourceWindow(EmulatorDiscovery discovery, GameTargetPreference saved)
    {
        _discovery = discovery;
        _saved = saved;
        InitializeComponent();
        AdbPath.Text = saved.AdbPath ?? "";
        Serial.Text = saved.Serial ?? "";
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _closed.Cancel();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var current = (Sources.SelectedItem as Source)?.Id ?? _saved.Id;
        var selectedWindow = WindowsList.SelectedItem as HostWindow;
        RefreshButton.IsEnabled = ApplyButton.IsEnabled = false;
        Status.Text = "正在查找模拟器…";
        try
        {
            var discovered = await Task.Run(() => _discovery.FindAsync(_closed.Token), _closed.Token);
            var windows = await Task.Run(GameTargetSelection.ListWindows, _closed.Token);
            if (_closed.IsCancellationRequested) return;

            var sources = new List<Source> { new(GameTarget.WindowsId, "Windows 客户端") };
            sources.AddRange(discovered.Select(t => new Source(t.Id,
                $"{(t.Kind == GameTargetKind.MuMu ? "MuMu" : "雷电")} · {t.Name}（实例 {t.Instance}）", t)));
            if (current != ManualId && sources.All(s => s.Id != current))
                sources.Add(new Source(current, "已保存的模拟器（窗口未启动）"));
            sources.Add(new Source(ManualId, "手动绑定 ADB（蓝叠、夜神、逍遥等）"));

            Sources.ItemsSource = sources;
            Sources.SelectedItem = sources.First(s => s.Id == current);
            WindowsList.ItemsSource = windows;
            WindowsList.SelectedItem = selectedWindow != null
                ? windows.FirstOrDefault(w => w.Handle == selectedWindow.Handle)
                : windows.FirstOrDefault(w => w.Title == _saved.WindowTitle &&
                    string.Equals(w.ProcessPath, _saved.ProcessPath, StringComparison.OrdinalIgnoreCase));
            Status.Text = "";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLogger.Error("Emulator", "Could not list game sources", ex);
            Status.Text = "读取游戏来源失败，请刷新重试。";
        }
        finally { RefreshButton.IsEnabled = ApplyButton.IsEnabled = true; }
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        ManualBinding.Visibility = Sources.SelectedItem is Source { Id: ManualId }
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBrowseAdb(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择模拟器或 Android SDK 的 ADB 程序", Filter = "ADB 程序|*.exe" };
        if (dialog.ShowDialog(this) == true) AdbPath.Text = dialog.FileName;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (Sources.SelectedItem is not Source source) return;
        if (source.Id == ManualId)
        {
            if (WindowsList.SelectedItem is not HostWindow window)
            {
                Status.Text = "请选择模拟器窗口。";
                return;
            }
            string adb = AdbPath.Text.Trim();
            string serial = Serial.Text.Trim();
            if (!Path.IsPathFullyQualified(adb) || !File.Exists(adb))
            {
                Status.Text = "请选择有效的 ADB 程序。";
                return;
            }
            if (!GameTargetSelection.IsLocalAdbSerial(serial))
            {
                Status.Text = "请输入本机 ADB 序列号。";
                return;
            }
            Preference = new GameTargetPreference(ManualId, window.Title, window.ProcessPath, adb, serial);
            Target = GameTargetSelection.FromManualWindow(Preference, window);
        }
        else
        {
            Preference = new GameTargetPreference(source.Id, _saved.WindowTitle, _saved.ProcessPath,
                _saved.AdbPath, _saved.Serial);
            Target = source.Id == GameTarget.WindowsId ? GameTargetSelection.FindWindowsClient() : source.Target;
        }
        DialogResult = true;
    }

    private sealed record Source(string Id, string Label, GameTarget? Target = null);
}
