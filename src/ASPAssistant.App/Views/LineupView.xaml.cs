using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Converters;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class LineupView : UserControl
{
    public event Action? NewRequested;
    public event Action? HistoryRequested;
    public event Action<Lineup>? EditRequested;
    public event Action<Lineup>? PreviewRequested;
    public event Action<Lineup>? TrackRequested;
    public LineupShareService? ShareService { get; set; }
    private Func<string, string?>? _findOperatorIcon;
    private Func<string, bool>? _isBanned;
    private bool _copying;
    private LineupSiteClient? _site;
    private Uri? _uploadedLineup;
    private bool _siteBusy;
    private readonly SemaphoreSlim _importGate = new(1, 1);
    internal bool IsImporting { get; private set; }
    private LineupLibraryViewModel? Vm => DataContext as LineupLibraryViewModel;

    public LineupView() => InitializeComponent();

    internal void ConfigurePortraits(Func<string, string?> findOperatorIcon, Func<string, bool> isBanned,
        Func<string, string?>? findStrategyIcon = null)
    {
        _findOperatorIcon = findOperatorIcon;
        ((StrategyCardPortraitsConverter)Resources["StrategyCardPortraits"]).FindIcon = findStrategyIcon;
        _isBanned = isBanned;
    }

    internal void ConfigureCovenantActivation(LineupCovenantCalculator calculator)
        => ((LineupActiveCovenantsConverter)Resources["LineupActiveCovenants"]).Calculator = calculator;

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
    private void OnHistory(object sender, RoutedEventArgs e) => HistoryRequested?.Invoke();
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

    internal void ConfigureSite(LineupSiteClient site) => _site = site;
    internal bool IsUploading => _siteBusy;

    private async void OnUpload(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Lineup { IsImported: false } lineup } button ||
            _siteBusy || _site is not { } site || ShareService is null)
            return;
        if (site.Email is null)
        {
            SetStatus("请在设置中登录卫戍阵容库，再上传阵容。");
            return;
        }

        _siteBusy = true;
        button.IsEnabled = false;
        try
        {
            var text = ExportShare(lineup);
            UploadedLineupButton.Visibility = Visibility.Collapsed;
            SetStatus("正在上传阵容…");
            _uploadedLineup = await site.UploadAsync(lineup.Id, text);
            UploadedLineupButton.Visibility = Visibility.Visible;
            SetStatus($"阵容“{lineup.Name}”已发布到卫戍阵容库。");
        }
        catch (Exception ex) when (ex is LineupSiteException or FormatException) { SetStatus(ex.Message); }
        finally
        {
            _siteBusy = false;
            button.IsEnabled = true;
        }
    }

    private void OnOpenUploadedLineup(object sender, RoutedEventArgs e)
    {
        if (_uploadedLineup is not null) OpenInBrowser(_uploadedLineup);
    }

    private void OnOpenLineupHub(object sender, RoutedEventArgs e) => OpenInBrowser(LineupPublicLink.HomePage);

    private void OpenInBrowser(Uri url)
    {
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception) { SetStatus("无法打开浏览器：" + url.AbsoluteUri); }
    }

    private string ExportShare(Lineup lineup) => ShareService!.Export(
        lineup with { Description = LineupDiagramSource.RemoveLegacyPositions(lineup.Description) });

    private void ShowShortLink(Window owner, string link)
    {
        if (!owner.IsVisible) return;
        var field = new TextBox { Text = link, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        System.Windows.Automation.AutomationProperties.SetName(field, "阵容短链接");
        var body = new DockPanel { Margin = new Thickness(12) };
        var hint = new TextBlock { Text = "按 Ctrl+C 复制选中的短链接。", Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(hint, Dock.Top);
        body.Children.Add(hint);
        var close = new Button { Content = "关闭", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        body.Children.Add(close);
        body.Children.Add(field);
        var dialog = new Window
        {
            Owner = owner, Title = "阵容短链接", Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
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
        if (Window.GetWindow(this) is { } owner) await ImportClipboardAsync(owner);
    }

    internal Task ImportClipboardAsync(Window owner, string? expectedHash = null) => ImportAsync(async () =>
    {
        if (expectedHash is null) return await LineupClipboard.ReadAsync(owner);
        // Windows may expand each line break to CRLF during a browser clipboard write.
        var text = await LineupClipboard.ReadAsync(owner, LineupShareService.MaxShareTextLength * 2);
        return LineupProtocol.RequireMatchingShare(text, expectedHash);
    }, wait: expectedHash is not null);

    internal Task ImportTextAsync(string text) => ImportAsync(() => Task.FromResult(text));

    private async Task ImportAsync(Func<Task<string>> readText, bool wait = false)
    {
        if (ShareService is null || Vm is not { } library || (IsImporting && !wait)) return;
        await _importGate.WaitAsync();
        IsImporting = true;
        ImportClipboardButton.IsEnabled = false;
        try
        {
            var text = await readText();
            string? link = null;
            if (LineupPublicLink.TryGetToken(text, out _))
            {
                library.Status = "正在读取阵容短链接…";
                link = LineupPublicLink.Canonicalize(text);
                text = await LineupPublicLink.ResolveAsync(link);
            }
            var imported = ShareService.Import(text) with { PublishedLink = link };
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
            _importGate.Release();
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
