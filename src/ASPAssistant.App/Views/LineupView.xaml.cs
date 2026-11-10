using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class LineupView : UserControl
{
    public event Action? NewRequested;
    public event Action<Lineup>? EditRequested;
    public event Action<Lineup>? PreviewRequested;
    public event Action<Lineup>? ImportPreviewRequested;
    public event Action<Lineup>? TrackRequested;
    public LineupShareService? ShareService { get; set; }
    private LineupLibraryViewModel? Vm => DataContext as LineupLibraryViewModel;

    public LineupView() => InitializeComponent();
    private void OnNew(object sender, RoutedEventArgs e) => NewRequested?.Invoke();
    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Lineup lineup) EditRequested?.Invoke(lineup);
    }

    private void OnPreview(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Lineup lineup) PreviewRequested?.Invoke(lineup);
    }

    private void OnTrackLineup(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Lineup lineup) TrackRequested?.Invoke(lineup);
    }

    private void OnFilterLineup(object sender, FilterEventArgs e)
        => e.Accepted = e.Item is Lineup lineup && MatchesCategory(lineup);

    private bool MatchesCategory(Lineup lineup) => CategoryTabs?.SelectedIndex switch
    {
        1 => !lineup.IsImported,
        2 => lineup.IsImported,
        _ => true
    };

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && Resources["FilteredLineups"] is CollectionViewSource source)
            source.View?.Refresh();
    }

    internal void RevealSavedLineup(Lineup lineup)
    {
        if (!MatchesCategory(lineup)) CategoryTabs.SelectedIndex = lineup.IsImported ? 2 : 1;
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Lineup lineup || ShareService is null) return;
        try
        {
            var share = lineup with { Description = LineupDiagramSource.RemoveLegacyPositions(lineup.Description) };
            Clipboard.SetText(ShareService.Export(share));
            SetStatus("阵容分享文本已复制到剪切板。");
        }
        catch (FormatException ex) { SetStatus(ex.Message); }
        catch (Exception ex) when (IsClipboardError(ex))
        {
            SetStatus("剪切板暂不可用，请稍后重试。");
        }
    }

    private void OnImportClipboard(object sender, RoutedEventArgs e)
    {
        try
        {
            ImportText(Clipboard.ContainsText() ? Clipboard.GetText() : "");
        }
        catch (Exception ex) when (IsClipboardError(ex))
        {
            SetStatus("无法读取剪切板，请稍后重试。");
        }
    }

    internal void ImportText(string text)
    {
        if (ShareService is null) return;
        try
        {
            var imported = ShareService.Import(text);
            ImportPreviewRequested?.Invoke(imported);
            SetStatus("已打开导入预览，可点击“编辑并保存”加入列表。");
        }
        catch (FormatException ex) { SetStatus(ex.Message); }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Lineup lineup || Vm is null) return;
        if (MessageBox.Show(Window.GetWindow(this), $"删除阵容“{lineup.Name}”？", "删除阵容", MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            await Vm.DeleteAsync(lineup);
    }

    private void SetStatus(string status) { if (Vm is not null) Vm.Status = status; }
    private static bool IsClipboardError(Exception ex) => ex is ExternalException or System.Threading.ThreadStateException;
}
