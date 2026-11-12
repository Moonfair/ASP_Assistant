using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class UpdateDialog : Window
{
    private readonly UpdateService _updateService;
    private readonly UpdateInfo _updateInfo;
    private CancellationTokenSource? _updateCancellation;
    private Task? _updateTask;
    private Task? _cancelUpdateTask;

    public UpdateDialog(UpdateService updateService, UpdateInfo updateInfo)
    {
        _updateService = updateService;
        _updateInfo = updateInfo;

        InitializeComponent();

        TitleText.Text = $"发现新版本 {updateInfo.TagName}";
        CurrentVersionText.Text = _updateService.IsDevBuild
            ? "0.0.0 (dev)"
            : _updateService.CurrentVersion.ToString(3);
        LatestVersionText.Text = updateInfo.Version;
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes)
            ? "（暂无更新说明）"
            : updateInfo.ReleaseNotes;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        // The installer is already running once the update task succeeds.
        if (_updateTask?.IsCompletedSuccessfully == true)
            e.Cancel = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_updateCancellation is { } cancellation && _updateTask?.IsCompletedSuccessfully != true)
            _cancelUpdateTask = cancellation.CancelAsync();

        base.OnClosed(e);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updateCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;

        UpdateButton.IsEnabled = false;
        DeferButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Text = "正在下载更新...";

        var progress = new Progress<double>(value =>
        {
            if (_updateCancellation != cancellation || cancellation.IsCancellationRequested ||
                _updateTask?.IsCompletedSuccessfully == true)
                return;

            DownloadProgress.Value = value * 100;
            ProgressPercent.Text = $"{(int)(value * 100)}%";
            StatusText.Text = value >= 0.9 ? "正在解压更新包..." : "正在下载更新...";
        });

        try
        {
            _updateTask = _updateService.DownloadAndApplyUpdateAsync(_updateInfo, progress, cancellation.Token);
            await _updateTask;

            StatusText.Text = "更新包已就绪，即将重启应用...";
            DownloadProgress.Value = 100;
            ProgressPercent.Text = "100%";

            await Task.Delay(1000);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested)
                ResetToIdle($"下载失败：{ex.Message}");
        }
        finally
        {
            if (_cancelUpdateTask is { } cancelTask)
            {
                try { await cancelTask; }
                catch (AggregateException ex) { AppLogger.Error("Updater", "Update cancellation callback failed", ex); }
            }

            _cancelUpdateTask = null;
            _updateTask = null;
            _updateCancellation = null;
        }
    }

    private void ResetToIdle(string statusMessage)
    {
        UpdateButton.IsEnabled = true;
        DeferButton.IsEnabled = true;
        StatusText.Text = statusMessage;
        ProgressPercent.Text = string.Empty;
        DownloadProgress.Value = 0;
    }
}
