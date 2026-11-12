using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    private readonly GarrisonHistoryClient _client = new();
    private readonly GarrisonHistoryLineupConverter _converter;
    private readonly LineupShareService _share;
    private readonly Func<Lineup, bool> _openEditor;
    private readonly Func<bool, string, string?> _findIcon;
    private readonly CancellationTokenSource _lifetime = new();
    private GarrisonSession? _session;
    private bool _closed;
    private bool _copying;
    private bool _opening;
    private bool _pulling;
    private bool _autoLoadPending;

    public GarrisonHistoryWindow(GarrisonHistoryLineupConverter converter, LineupShareService share,
        Func<Lineup, bool> openEditor, Func<bool, string, string?> findIcon)
    {
        _converter = converter;
        _share = share;
        _openEditor = openEditor;
        _findIcon = findIcon;
        InitializeComponent();
        Browser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ASPAssistant", "PassportWebView")
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
        if (_opening || _pulling) return;
        _opening = true;
        Status.Text = "正在打开鹰角官网…";
        try
        {
            var webView = Browser.CoreWebView2;
            if (webView is null)
            {
                await Browser.EnsureCoreWebView2Async();
                if (_closed) return;
                webView = Browser.CoreWebView2 ?? throw new InvalidOperationException();
                webView.Settings.AreHostObjectsAllowed = false;
                webView.Settings.IsWebMessageEnabled = false;
                webView.NavigationStarting += OnNavigationStarting;
                webView.NavigationCompleted += OnNavigationCompleted;
                webView.NewWindowRequested += OnNewWindowRequested;
                webView.WebResourceResponseReceived += OnPassportResponse;
            }
            webView.Navigate(HypergryphPassport.LoginUri.AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            if (!_closed) Status.Text = "请安装 Microsoft Edge WebView2 运行时后，再打开鹰角官网。";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException)
        {
            if (!_closed) Status.Text = "无法打开鹰角官网，请重试。";
        }
        finally { _opening = false; }
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        if (_pulling || _opening) return;
        _autoLoadPending = false;
        ResetSession();
        Records.ItemsSource = null;
        await OpenSourceAsync();
    }

    private void ResetSession()
    {
        _session = null;
        RolePicker.ItemsSource = null;
        RolePicker.Visibility = Visibility.Collapsed;
    }

    private static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        => e.Cancel = !HypergryphPassport.IsPassportPage(e.Uri);

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_closed || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
        if (e.IsSuccess) QueueAutoLoad();
        else if (!_pulling) Status.Text = "鹰角官网加载失败，请检查网络后重新打开。";
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.IsUserInitiated && HypergryphPassport.IsPassportPage(e.Uri) && sender is CoreWebView2 webView)
            webView.Navigate(e.Uri);
    }

    private void OnPassportResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (!_closed && sender is CoreWebView2 webView &&
            HypergryphPassport.IsPassportPage(webView.Source) &&
            HypergryphPassport.IsSessionSaved(e.Request.Uri, e.Request.Method, e.Response.StatusCode))
            QueueAutoLoad();
    }

    private void QueueAutoLoad()
    {
        if (_closed || _session is not null) return;
        _autoLoadPending = true;
        if (!_pulling) _ = LoadAfterPassportSignalAsync();
    }

    private async Task LoadAfterPassportSignalAsync()
    {
        while (_autoLoadPending && !_closed && _session is null && !_pulling)
        {
            _autoLoadPending = false;
            await LoadHistoryAsync();
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            HistoryTabs.SelectedItem = SourceTab;
            QueueAutoLoad();
            return;
        }

        await LoadHistoryAsync();
    }

    private async void OnRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pulling || _session is null || RolePicker.SelectedItem is not GarrisonRole) return;
        Records.ItemsSource = null;
        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        if (_closed || _pulling || Browser.CoreWebView2 is not { } webView) return;
        _pulling = true;
        RefreshButton.IsEnabled = false;
        ReloadButton.IsEnabled = false;
        RolePicker.IsEnabled = false;
        var cancellationToken = _lifetime.Token;
        try
        {
            if (_session is null)
            {
                Status.Text = "正在检查鹰角登录状态…";
                var code = await HypergryphPassport.RequestHistoryGrantAsync(webView, cancellationToken);
                if (_closed) return;
                if (string.IsNullOrWhiteSpace(code))
                {
                    Status.Text = "";
                    return;
                }

                HistoryTabs.SelectedItem = RecordsTab;
                Status.Text = "正在读取明日方舟角色…";
                var session = await _client.AuthorizeAsync(code, cancellationToken);
                if (_closed) return;
                _session = session;
                _autoLoadPending = false;
                RolePicker.ItemsSource = session.Roles;
                RolePicker.SelectedIndex = session.Roles.Count > 0 ? 0 : -1;
                RolePicker.Visibility = session.Roles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (RolePicker.SelectedItem is not GarrisonRole role)
            {
                ShowHistoryRows([], "这个鹰角通行证没有可读取战绩的明日方舟角色。");
                return;
            }

            Status.Text = "正在拉取战绩…";
            var result = await _client.FetchAsync(_session, role, cancellationToken);
            var entries = await ConvertRowsAsync(result, cancellationToken);
            if (!_closed)
                ShowHistoryRows(entries, entries.Count == 0 ? "这个角色当前赛季没有战绩。" : $"已拉取 {entries.Count} 场战绩。");
        }
        catch (GarrisonAuthorizationException ex)
        {
            if (_closed) return;
            ResetSession();
            Status.Text = ex.Message;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or TimeoutException)
        {
            if (!_closed) Status.Text = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or COMException or OperationCanceledException or JsonException)
        {
            if (!_closed) Status.Text = "读取战绩或赛季资料失败，请检查网络后重试。";
        }
        finally
        {
            _pulling = false;
            if (!_closed)
            {
                RefreshButton.IsEnabled = HistoryTabs.SelectedItem == RecordsTab;
                ReloadButton.IsEnabled = true;
                RolePicker.IsEnabled = true;
            }
        }
    }

    private void ShowHistoryRows(IReadOnlyList<HistoryRow> entries, string status)
    {
        var selectedId = SelectedRow?.Record.Id;
        Records.ItemsSource = entries;
        Records.SelectedItem = entries.FirstOrDefault(entry => entry.Record.Id == selectedId) ?? entries.FirstOrDefault();
        HistoryTabs.SelectedItem = RecordsTab;
        Status.Text = status;
    }


    private async Task<List<HistoryRow>> ConvertRowsAsync(GarrisonHistoryResult result, CancellationToken cancellationToken)
    {
        var entries = new List<HistoryRow>(result.Records.Count);
        var catalogs = new Dictionary<(string?, string?), GarrisonCatalog?>();
        var current = result.Catalog;
        foreach (var record in result.Records)
        {
            var key = (record.SeasonId ?? current.SeasonId, record.VersionId ?? current.VersionId);
            if (!catalogs.TryGetValue(key, out var catalog))
            {
                catalog = key == (current.SeasonId, current.VersionId)
                    ? current
                    : await LoadCatalogAsync(key.Item1, key.Item2, cancellationToken);
                catalogs.Add(key, catalog);
            }
            var export = catalog is null
                ? new GarrisonLineupExport(null, ["该赛季资料暂时无法读取，请重新拉取战绩。"])
                : _converter.Convert(record, catalog);
            entries.Add(new HistoryRow(record, export));
        }
        return entries;
    }

    private async Task<GarrisonCatalog?> LoadCatalogAsync(string? season, string? version, CancellationToken cancellationToken)
    {
        try { return await _client.ReadCatalogAsync(season, version, cancellationToken); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException or GarrisonAuthorizationException ||
                                   ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }


    private HistoryRow? SelectedRow => Records.SelectedItem as HistoryRow;

    private void OnRecordSelected(object sender, SelectionChangedEventArgs e)
    {
        var row = SelectedRow;
        var lineup = row?.Export.Lineup;
        RecordTitle.Text = row?.Title ?? "选择一场历史对局";
        RecordStrategy.Text = lineup?.AvailableStrategies.FirstOrDefault() is { } strategy ? "策略 · " + strategy : "";
        Members.ItemsSource = lineup?.Members.Select(member => new HistoryMember(
            member.OperatorName, member.IsElite, _findIcon(false, member.OperatorName),
            member.EquipmentSlots.Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => new HistoryEquipment(name, _findIcon(true, name))).ToArray())).ToArray();
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
        _session = null;
        if (Browser.CoreWebView2 is { } webView)
            webView.WebResourceResponseReceived -= OnPassportResponse;
        Browser.Dispose();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private sealed record HistoryRow(GarrisonRecord Record, GarrisonLineupExport Export)
    {
        public string Time => Record.EndedAt?.ToLocalTime().ToString("MM-dd HH:mm") ?? "历史战绩";
        public string Rounds => Record.PassedRounds is { } rounds ? $"{rounds} 回合" : "";
        public string ModeAndDifficulty => Join(Record.ModeName, Record.Difficulty switch
        {
            "FUNNY" => "趣味", "NORMAL" => "标准", "HARD" => "困难", "ABYSS" => "绝境", _ => Record.Difficulty
        });
        public string Title => Join(Time, ModeAndDifficulty, Rounds);

        private static string Join(params string?[] parts) =>
            string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private sealed record HistoryMember(string Name, bool IsElite, string? IconPath, IReadOnlyList<HistoryEquipment> Equipment);
    private sealed record HistoryEquipment(string Name, string? IconPath);
}
