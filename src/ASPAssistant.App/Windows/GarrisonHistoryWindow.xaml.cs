using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ASPAssistant.App.Windows;

public partial class GarrisonHistoryWindow : Window
{
    private static readonly HttpClient CatalogClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = 8 * 1024 * 1024
    };

    private readonly GarrisonHistoryLineupConverter _converter;
    private readonly LineupShareService _share;
    private readonly Func<Lineup, bool> _openEditor;
    private readonly CancellationTokenSource _lifetime = new();
    private int _requestNumber;
    private bool _closed;
    private bool _copying;
    private bool _opening;

    public GarrisonHistoryWindow(GarrisonHistoryLineupConverter converter, LineupShareService share, Func<Lineup, bool> openEditor)
    {
        _converter = converter;
        _share = share;
        _openEditor = openEditor;
        InitializeComponent();
        Browser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ASPAssistant", "SklandWebView")
        };
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await OpenSourceAsync();
    }

    private async Task OpenSourceAsync()
    {
        if (_opening) return;
        _opening = true;
        _requestNumber++;
        Status.Text = "正在打开森空岛…";
        try
        {
            var webView = Browser.CoreWebView2;
            if (webView is null)
            {
                await Browser.EnsureCoreWebView2Async();
                if (_closed) return;
                webView = Browser.CoreWebView2 ?? throw new InvalidOperationException("WebView2 initialization returned no browser.");
                webView.Settings.AreHostObjectsAllowed = false;
                webView.Settings.IsWebMessageEnabled = false;
                webView.NavigationStarting += OnNavigationStarting;
                webView.NavigationCompleted += OnNavigationCompleted;
                webView.NewWindowRequested += OnNewWindowRequested;
                webView.WebResourceResponseReceived += OnWebResponse;
            }
            webView.Navigate(GarrisonHistory.EditorUri.AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            if (!_closed) Status.Text = "请安装 Microsoft Edge WebView2 运行时后，再重新打开森空岛。";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException)
        {
            if (!_closed) Status.Text = "无法打开森空岛，请重试。";
        }
        finally { _opening = false; }
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        try { await OpenSourceAsync(); }
        finally { button.IsEnabled = true; }
    }

    private static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        => e.Cancel = !IsSklandPage(e.Uri);

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_closed || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
        Status.Text = e.IsSuccess ? "" : "森空岛页面加载失败，请检查网络后重新打开。";
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.IsUserInitiated && IsSklandPage(e.Uri) && sender is CoreWebView2 webView)
            webView.Navigate(e.Uri);
    }

    private static bool IsSklandPage(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        (uri.Host is "skland.com" or "hypergryph.com" ||
         uri.Host.EndsWith(".skland.com", StringComparison.Ordinal) ||
         uri.Host.EndsWith(".hypergryph.com", StringComparison.Ordinal));

    private async void OnWebResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (_closed || sender is not CoreWebView2 webView ||
            !Uri.TryCreate(webView.Source, UriKind.Absolute, out var source) ||
            source.GetLeftPart(UriPartial.Path) != GarrisonHistory.EditorUri.AbsoluteUri ||
            e.Request.Method != "GET" || !GarrisonHistory.IsHistoryEndpoint(e.Request.Uri))
            return;

        var request = ++_requestNumber;
        var cancellationToken = _lifetime.Token;
        Status.Text = "正在读取战绩并匹配阵容…";
        try
        {
            if (e.Response.StatusCode == 401) throw new FormatException("请先在森空岛登录，再重新拉取战绩。");
            if (e.Response.StatusCode != 200) throw new FormatException("森空岛战绩请求失败，请稍后重试。");
            using var content = await e.Response.GetContentAsync();
            if (content is null) throw new FormatException("没有收到森空岛战绩响应，请重新拉取。");
            var records = await GarrisonHistory.ReadRecordsAsync(content, cancellationToken);
            var entries = new List<HistoryRow>(records.Count);
            var catalogs = new Dictionary<(string?, string?), GarrisonCatalog?>();
            foreach (var record in records)
            {
                if (!IsCurrent(request)) return;
                var key = (record.SeasonId, record.VersionId);
                if (!catalogs.TryGetValue(key, out var catalog))
                {
                    catalog = await LoadCatalogAsync(key.SeasonId, key.VersionId, cancellationToken);
                    catalogs.Add(key, catalog);
                }
                var export = catalog is null
                    ? new GarrisonLineupExport(null, ["该赛季资料暂时无法读取，请重新拉取战绩。"])
                    : _converter.Convert(record, catalog);
                entries.Add(new HistoryRow(record, export));
            }

            if (!IsCurrent(request)) return;
            Records.ItemsSource = entries;
            Records.SelectedIndex = entries.Count == 0 ? -1 : 0;
            HistoryTabs.SelectedItem = RecordsTab;
            Status.Text = entries.Count == 0 ? "这个角色当前赛季没有战绩。" : $"已拉取 {entries.Count} 场战绩。";
        }
        catch (FormatException ex)
        {
            if (IsCurrent(request)) Status.Text = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or COMException or OperationCanceledException)
        {
            if (IsCurrent(request)) Status.Text = "读取战绩或赛季资料失败，请检查网络后重试。";
        }
    }

    private static async Task<GarrisonCatalog?> LoadCatalogAsync(string? season, string? version, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await CatalogClient.GetAsync(GarrisonHistory.CatalogUri(season, version), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var resource = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await GarrisonHistory.ReadCatalogAsync(resource, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException ||
                                   ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private bool IsCurrent(int request) => !_closed && request == _requestNumber;
    private HistoryRow? SelectedRow => Records.SelectedItem as HistoryRow;

    private void OnRecordSelected(object sender, SelectionChangedEventArgs e)
    {
        var row = SelectedRow;
        var lineup = row?.Export.Lineup;
        RecordTitle.Text = row?.Title ?? "选择一场历史对局";
        RecordStrategy.Text = lineup?.AvailableStrategies.FirstOrDefault() is { } strategy ? "策略 · " + strategy : "";
        Members.ItemsSource = lineup?.Members;
        var notes = row?.Export.Omissions ?? [];
        Omissions.Text = lineup is null && row is not null && notes.Count == 0
            ? "这场战绩没有可导出的干员。"
            : string.Join(Environment.NewLine, notes);
        CopyButton.IsEnabled = lineup is not null && !_copying;
        EditButton.IsEnabled = lineup is not null;
    }

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_copying || SelectedRow?.Export.Lineup is not { } lineup) return;
        _copying = true;
        CopyButton.IsEnabled = false;
        try
        {
            await LineupClipboard.CopyAsync(this, _share.Export(lineup));
            if (!_closed) Status.Text = "已复制阵容分享文本，可在 ASP Assistant 中导入。";
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or FormatException)
        {
            if (!_closed) Status.Text = ex is FormatException ? ex.Message : "剪切板暂不可用，请重试。";
        }
        finally
        {
            _copying = false;
            if (!_closed) CopyButton.IsEnabled = SelectedRow?.Export.Lineup is not null;
        }
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (SelectedRow?.Export.Lineup is not { } lineup) return;
        if (_openEditor(lineup with { Id = Guid.NewGuid() }))
            Status.Text = "已在阵容编辑器中打开，保存后可在阵容库使用。";
        else
            Status.Text = "请先完成当前正在编辑的阵容。";
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        if (Browser.CoreWebView2 is { } webView)
            webView.WebResourceResponseReceived -= OnWebResponse;
        Browser.Dispose();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private sealed record HistoryRow(GarrisonRecord Record, GarrisonLineupExport Export)
    {
        public string Title => string.Join(" · ", new[]
        {
            Record.EndedAt?.ToLocalTime().ToString("MM-dd HH:mm") ?? "历史战绩",
            Record.ModeName,
            Record.Difficulty switch
            {
                "FUNNY" => "趣味", "NORMAL" => "标准", "HARD" => "困难", "ABYSS" => "绝境", _ => Record.Difficulty
            },
            Record.PassedRounds is { } rounds ? $"{rounds} 回合" : null
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
