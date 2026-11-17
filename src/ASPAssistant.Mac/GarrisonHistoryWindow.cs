using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Mac;

internal sealed class GarrisonHistoryWindow : Window
{
    private readonly MacWorkspace _workspace;
    private readonly GarrisonHistoryClient _client = new();
    private readonly GarrisonHistoryLineupConverter _converter;
    private readonly LineupShareService _share;
    private readonly CancellationTokenSource _closed = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = MacPalette.Secondary };
    private readonly ComboBox _roles = new()
    {
        MinWidth = 200,
        ItemTemplate = new FuncDataTemplate<GarrisonRole>((role, _) =>
            new TextBlock { Text = role.DisplayName })
    };
    private readonly ListBox _records = new();
    private readonly StackPanel _details = new() { Spacing = 7 };
    private GarrisonSession? _session;
    private bool _busy;

    public GarrisonHistoryWindow(MacWorkspace workspace)
    {
        _workspace = workspace;
        _converter = new GarrisonHistoryLineupConverter(workspace.Operators,
            workspace.Equipment.Select(item => item.Name), workspace.Strategies.Select(item => item.Name));
        _share = LineupShareService.FromCatalog(workspace.Operators,
            workspace.Equipment.Select(item => item.Name), workspace.OperatorCatalog);
        Title = "卫戍战绩";
        Width = 880;
        Height = 680;
        MinWidth = 700;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closed += (_, _) => _closed.Cancel();

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(new TextBlock { Text = "卫戍战绩", FontSize = 21, FontWeight = FontWeight.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "使用鹰角通行证读取战绩，选择角色后可复制或保存阵容。",
            Foreground = MacPalette.Secondary, TextWrapping = TextWrapping.Wrap
        });
        var actions = new WrapPanel();
        var authorize = new Button { Content = "鹰角登录 / 授权" };
        authorize.Click += async (_, _) => await AuthorizeAsync();
        actions.Children.Add(authorize);
        _roles.SelectionChanged += async (_, _) =>
        {
            if (!_busy && _roles.SelectedItem is GarrisonRole) await LoadRecordsAsync();
        };
        actions.Children.Add(_roles);
        var refresh = new Button { Content = "刷新战绩" };
        refresh.Click += async (_, _) => await LoadRecordsAsync();
        actions.Children.Add(refresh);
        header.Children.Add(actions);
        header.Children.Add(_status);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*"), Margin = new Thickness(0, 12, 0, 0) };
        _records.SelectionChanged += (_, _) => RenderSelection();
        grid.Children.Add(_records);
        var detailScroll = new ScrollViewer { Content = _details, Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(detailScroll, 1);
        grid.Children.Add(detailScroll);
        root.Children.Add(grid);
        Content = root;
        Opened += async (_, _) => await AuthorizeAsync();
    }

    private async Task AuthorizeAsync()
    {
        if (_busy) return;
        _busy = true;
        _status.Text = "请在打开的鹰角通行证窗口登录并点击「读取战绩授权」。";
        try
        {
            var code = await MacPassportGrant.RequestAsync(_closed.Token);
            if (string.IsNullOrWhiteSpace(code)) { _status.Text = "授权窗口已关闭。"; return; }
            _status.Text = "正在读取明日方舟角色…";
            _session = await _client.AuthorizeAsync(code, _closed.Token);
            _roles.ItemsSource = _session.Roles;
            _roles.SelectedIndex = _session.Roles.Count > 0 ? 0 : -1;
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _busy = false; }
        if (_session?.Roles.Count > 0) await LoadRecordsAsync();
    }

    private async Task LoadRecordsAsync()
    {
        if (_busy || _session is null || _roles.SelectedItem is not GarrisonRole role) return;
        _busy = true;
        _status.Text = "正在拉取战绩…";
        try
        {
            var result = await _client.FetchAsync(_session, role, _closed.Token);
            var rows = new List<HistoryRow>();
            var catalogs = new Dictionary<(string?, string?), GarrisonCatalog?>();
            foreach (var record in result.Records)
            {
                var key = (record.SeasonId ?? result.Catalog.SeasonId,
                    record.VersionId ?? result.Catalog.VersionId);
                if (!catalogs.TryGetValue(key, out var catalog))
                {
                    try
                    {
                        catalog = key == (result.Catalog.SeasonId, result.Catalog.VersionId)
                            ? result.Catalog : await _client.ReadCatalogAsync(key.Item1, key.Item2, _closed.Token);
                    }
                    catch (Exception) { catalog = null; }
                    catalogs[key] = catalog;
                }
                rows.Add(new HistoryRow(record, catalog is null
                    ? new GarrisonLineupExport(null, ["该赛季资料暂时无法读取。"])
                    : _converter.Convert(record, catalog)));
            }
            _records.ItemsSource = rows;
            _records.SelectedIndex = rows.Count > 0 ? 0 : -1;
            _status.Text = rows.Count == 0 ? "当前赛季没有战绩。" : $"已拉取 {rows.Count} 场战绩。";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { _status.Text = "读取战绩失败：" + ex.Message; }
        finally { _busy = false; }
    }

    private void RenderSelection()
    {
        _details.Children.Clear();
        if (_records.SelectedItem is not HistoryRow row) return;
        _details.Children.Add(new TextBlock { Text = row.ToString(), FontSize = 17, FontWeight = FontWeight.Bold });
        if (row.Export.Lineup is not { } lineup)
        {
            _details.Children.Add(new TextBlock { Text = string.Join("\n", row.Export.Omissions) });
            return;
        }
        if (lineup.AvailableStrategies.FirstOrDefault() is { } strategy)
            _details.Children.Add(new TextBlock { Text = "策略：" + strategy });
        foreach (var member in lineup.Members)
        {
            var items = string.Join("、", member.EquipmentSlots.Where(item => !string.IsNullOrWhiteSpace(item)));
            _details.Children.Add(new TextBlock
            {
                Text = member.OperatorName + (member.IsElite ? "（精锐）" : "") +
                    (items.Length == 0 ? "" : " · " + items), TextWrapping = TextWrapping.Wrap
            });
        }
        if (row.Export.Omissions.Count > 0)
            _details.Children.Add(new TextBlock { Text = string.Join("\n", row.Export.Omissions), Foreground = Brushes.Orange });
        var actions = new WrapPanel();
        var save = new Button { Content = "保存为本地阵容" };
        save.Click += async (_, _) =>
        {
            try
            {
                await _workspace.Lineups.SaveAsync(lineup with { Id = Guid.NewGuid() });
                _status.Text = "已保存到本地阵容库。";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        actions.Children.Add(save);
        var copy = new Button { Content = "复制分享文本" };
        copy.Click += async (_, _) =>
        {
            try
            {
                await Clipboard!.SetTextAsync(_share.Export(lineup));
                _status.Text = "阵容分享文本已复制。";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        actions.Children.Add(copy);
        _details.Children.Add(actions);
    }

    private sealed record HistoryRow(GarrisonRecord Record, GarrisonLineupExport Export)
    {
        public override string ToString() => string.Join(" · ", new[]
        {
            Record.EndedAt?.ToLocalTime().ToString("MM-dd HH:mm") ?? "历史战绩",
            Record.ModeName,
            Record.Difficulty switch { "FUNNY" => "趣味", "NORMAL" => "标准", "HARD" => "困难", "ABYSS" => "绝境", _ => Record.Difficulty ?? "" },
            Record.PassedRounds is { } rounds ? $"{rounds} 回合" : ""
        }.Where(item => item.Length > 0));
    }
}
