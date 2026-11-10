using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class LineupView : UserControl
{
    public event Action? NewRequested;
    public event Action<Lineup>? EditRequested;
    public event Action<Lineup>? PreviewRequested;
    public event Action<Lineup>? TrackRequested;
    public LineupShareService? ShareService { get; set; }
    private Func<string, string?>? _findOperatorIcon;
    private Func<string, bool>? _isBanned;
    private bool _copying;
    internal bool IsImporting { get; private set; }
    private LineupLibraryViewModel? Vm => DataContext as LineupLibraryViewModel;

    public LineupView() => InitializeComponent();

    internal void ConfigurePortraits(Func<string, string?> findOperatorIcon, Func<string, bool> isBanned)
    {
        _findOperatorIcon = findOperatorIcon;
        _isBanned = isBanned;
    }

    private void OnPortraitLoaded(object sender, RoutedEventArgs e) => UpdatePortrait((Image)sender);

    private void OnPortraitMemberChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdatePortrait((Image)sender);

    private void UpdatePortrait(Image portrait)
    {
        var name = (portrait.DataContext as LineupMember)?.OperatorName;
        DeferredIcon.SetIsGrayscale(portrait, name is not null && _isBanned?.Invoke(name) == true);
        DeferredIcon.SetPath(portrait, name is null ? "" : _findOperatorIcon?.Invoke(name) ?? "");
    }

    internal void RefreshBannedPortraits()
    {
        if (_isBanned is null) return;
        foreach (var portrait in FindPortraits(LineupCards))
            if (portrait.DataContext is LineupMember member)
                DeferredIcon.SetIsGrayscale(portrait, _isBanned(member.OperatorName));
    }

    private static IEnumerable<Image> FindPortraits(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Image portrait) yield return portrait;
            else
                foreach (var descendant in FindPortraits(child)) yield return descendant;
        }
    }

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

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Lineup lineup || ShareService is null ||
            Window.GetWindow(this) is not { } owner || _copying) return;
        _copying = true;
        try
        {
            var share = lineup with { Description = LineupDiagramSource.RemoveLegacyPositions(lineup.Description) };
            var text = ShareService.Export(share);
            SetStatus("正在复制分享文本…");
            try { await LineupClipboard.CopyAsync(owner, text); }
            catch (Exception ex) when (IsClipboardError(ex))
            {
                SetStatus(ex.HResult == LineupClipboard.CannotOpenClipboard
                    ? "剪贴板仍被占用，已打开分享文本。" : "无法写入剪贴板，已打开分享文本。");
                ShowShareText(owner, text);
                return;
            }
            SetStatus("阵容分享文本已复制到剪切板。");
        }
        catch (FormatException ex) { SetStatus(ex.Message); }
        finally { _copying = false; }
    }

    private void ShowShareText(Window owner, string text)
    {
        if (!owner.IsVisible) return;
        var field = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        System.Windows.Automation.AutomationProperties.SetName(field, "阵容分享文本");
        var body = new DockPanel { Margin = new Thickness(12) };
        var hint = new TextBlock { Text = "剪贴板可用后，按 Ctrl+C 复制选中的分享文本。", Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(hint, Dock.Top);
        body.Children.Add(hint);
        var close = new Button { Content = "关闭", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        body.Children.Add(close);
        body.Children.Add(field);
        var dialog = new Window
        {
            Owner = owner, Title = "阵容分享文本", Width = 560, Height = 380, MinWidth = 350, MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = (Brush)FindResource("PanelBackgroundBrush"), Content = body
        };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/ASPAssistant.App;component/Resources/LineupStyles.xaml", UriKind.Relative)
        });
        dialog.Loaded += (_, _) => { field.Focus(); field.SelectAll(); };
        dialog.ShowDialog();
    }

    private async void OnImportClipboard(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner)
            await ImportAsync(() => LineupClipboard.ReadAsync(owner));
    }

    internal Task ImportTextAsync(string text) => ImportAsync(() => Task.FromResult(text));

    private async Task ImportAsync(Func<Task<string>> readText)
    {
        if (ShareService is null || Vm is not { } library || IsImporting) return;
        IsImporting = true;
        ImportClipboardButton.IsEnabled = false;
        try
        {
            var imported = ShareService.Import(await readText());
            imported = imported with { Description = LineupDiagramSource.RemoveLegacyPositions(imported.Description) };
            library.Status = "正在保存导入的阵容…";
            if (!await library.SaveAsync(imported)) return;
            CategoryTabs.SelectedIndex = 2;
            library.Status = $"已导入阵容“{imported.Name}”。";
        }
        catch (FormatException ex) { library.Status = ex.Message; }
        catch (Exception ex) when (IsClipboardError(ex))
        {
            library.Status = ex.HResult == LineupClipboard.CannotOpenClipboard
                ? "剪贴板仍被占用，请稍后重试。" : "无法读取剪贴板，请稍后重试。";
        }
        finally
        {
            IsImporting = false;
            ImportClipboardButton.IsEnabled = true;
        }
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
