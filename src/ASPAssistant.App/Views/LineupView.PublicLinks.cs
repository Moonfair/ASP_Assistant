using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Views;

public partial class LineupView
{
    private async void OnSharePublicLink(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Lineup lineup } button || ShareService is null
            || Window.GetWindow(this) is not { } owner || _copying) return;
        _copying = true;
        button.IsEnabled = false;
        try
        {
            var text = await ExportShareAsync(lineup);
            SetStatus("正在生成阵容短链接…");
            var link = await LineupPublicLink.CreateAsync(text);
            try
            {
                await LineupClipboard.CopyAsync(owner, link);
                SetStatus("阵容短链接已复制，90 天内有效。");
            }
            catch (Exception ex) when (IsClipboardError(ex))
            {
                SetStatus("短链接已生成。剪贴板暂不可用，请手动复制。");
                ShowShortLink(owner, link);
            }
        }
        catch (FormatException ex) { SetStatus(ex.Message); }
        finally
        {
            button.IsEnabled = true;
            _copying = false;
        }
    }
}
