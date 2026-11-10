using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class UpdateDialog : Window
{
    private readonly UpdateService _updateService;
    private readonly UpdateInfo _updateInfo;
    private bool _isDownloading;

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

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (!_isDownloading)
            Close();
    }

    private void OnDefer(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_isDownloading) return;
        _isDownloading = true;

        UpdateButton.IsEnabled = false;
        DeferButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        var cts = new CancellationTokenSource();
        var progress = new Progress<double>(value =>
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgress.Value = value * 100;
                ProgressPercent.Text = $"{(int)(value * 100)}%";

                if (value >= 0.9 && value < 1.0)
                    StatusText.Text = "正在解压更新包...";
                else if (value >= 1.0)
                    StatusText.Text = "准备重启...";
            });
        });

        try
        {
            await _updateService.DownloadAndApplyUpdateAsync(_updateInfo, progress, cts.Token);

            StatusText.Text = "更新包已就绪，即将重启应用...";
            DownloadProgress.Value = 100;
            ProgressPercent.Text = "100%";

            await Task.Delay(1000);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            ResetToIdle("下载已取消。");
        }
        catch (Exception ex)
        {
            ResetToIdle($"下载失败：{ex.Message}");
        }
    }

    private void ResetToIdle(string statusMessage)
    {
        _isDownloading = false;
        UpdateButton.IsEnabled = true;
        DeferButton.IsEnabled = true;
        StatusText.Text = statusMessage;
        ProgressPercent.Text = string.Empty;
        DownloadProgress.Value = 0;
    }
}
