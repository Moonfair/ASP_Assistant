using System.Reflection;
using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class UpdateDialog : Window
{
    public UpdateDialog(UpdateInfo updateInfo)
    {
        InitializeComponent();

        TitleText.Text = $"发现新版本 {updateInfo.TagName}";

        var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentVersionText.Text = currentVersion == new Version(0, 0, 0) || currentVersion is null
            ? "0.0.0 (dev)"
            : currentVersion.ToString(3);

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

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
