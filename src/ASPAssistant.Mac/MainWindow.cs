using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Mac;

public sealed class MainWindow : Window
{
    private const string CapturePermissionStatus = "识别已暂停 · 请在设置中授权屏幕录制并重启应用";
    private MacWorkspace? _workspace;
    private MarkerWindow? _markerWindow;
    private readonly TextBlock _source = new() { FontSize = 11, Foreground = MacPalette.Secondary };
    private readonly TextBlock _status = new() { Text = "正在载入数据…", FontSize = 11,
        Foreground = MacPalette.Secondary, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { FontSize = 12, Foreground = MacPalette.Text,
        TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _tabs = new() { FontSize = 13 };
    private bool _loading;

    public bool FollowGameWindow { get; private set; } = true;

    public MainWindow()
    {
        Title = "ASP Assistant";
        Width = 480;
        Height = 760;
        MinWidth = 410;
        MinHeight = 540;
        Topmost = true;
        MacPalette.Apply(false);
        Background = MacPalette.Window;
        Foreground = MacPalette.Text;
        ActualThemeVariantChanged += (_, _) =>
            MacPalette.Apply(ActualThemeVariant == ThemeVariant.Dark);

        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock
        {
            Text = "卫戍协议", FontSize = 17, FontWeight = FontWeight.SemiBold,
            Foreground = MacPalette.Text
        });
        header.Children.Add(_source);
        var scan = new Button
        {
            Content = "立即识别", FontSize = 12, Padding = new Thickness(12, 6),
            Background = MacPalette.Accent, Foreground = MacPalette.AccentText,
            CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center
        };
        scan.Click += (_, _) =>
        {
            if (_workspace == null) return;
            if (_workspace.Capture == null)
            {
                _status.Text = CapturePermissionStatus;
                return;
            }
            _workspace.ScanNow();
        };
        var toolbarContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        toolbarContent.Children.Add(header);
        Grid.SetColumn(scan, 1);
        toolbarContent.Children.Add(scan);
        var toolbar = new Border
        {
            Background = MacPalette.Toolbar, BorderBrush = MacPalette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 12),
            Child = toolbarContent
        };
        var summary = new Border
        {
            Background = MacPalette.Surface, BorderBrush = MacPalette.Border,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9), Margin = new Thickness(14, 12, 14, 4),
            Child = _summary
        };
        var footer = new Border
        {
            Background = MacPalette.Toolbar, BorderBrush = MacPalette.Border,
            BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(15, 8),
            Child = _status
        };
        _tabs.Margin = new Thickness(14, 2, 14, 10);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        root.Children.Add(toolbar);
        Grid.SetRow(summary, 1); root.Children.Add(summary);
        Grid.SetRow(_tabs, 2); root.Children.Add(_tabs);
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        Content = root;
        Opened += async (_, _) =>
        {
            MacPalette.Apply(ActualThemeVariant == ThemeVariant.Dark);
            await LoadWorkspaceAsync();
        };
        Closed += (_, _) =>
        {
            _markerWindow?.Close();
            _workspace?.Dispose();
        };
    }

    public void SetFollowGameWindow(bool enabled)
    {
        FollowGameWindow = enabled;
        if (enabled && _workspace?.WindowTracker.CurrentGameRect is { } rect)
            PositionBeside(rect);
    }

    private async Task LoadWorkspaceAsync()
    {
        if (_loading || _workspace != null) return;
        _loading = true;
        try
        {
            var canCapture = MacScreenPermission.IsGranted;
            _workspace = await MacWorkspace.LoadAsync(initializeScanner: canCapture);
            var workspace = _workspace;
            _markerWindow = new MarkerWindow();
            workspace.ScannerStatusChanged += () => Dispatcher.UIThread.Post(() =>
                _status.Text = canCapture ? workspace.ScannerStatus : CapturePermissionStatus);
            workspace.GameSource.Changed += () => Dispatcher.UIThread.Post(() => UpdateSourceStatus(workspace));
            workspace.GameStateChanged += () => Dispatcher.UIThread.Post(() =>
            {
                UpdateGameSummary(workspace);
                _markerWindow?.Update(workspace);
            });
            workspace.Tracking.TrackingChanged += () =>
            {
                _ = SaveTrackingAsync(workspace);
                Dispatcher.UIThread.Post(() => _markerWindow?.Update(workspace));
            };
            workspace.WindowTracker.GameWindowMoved += rect => Dispatcher.UIThread.Post(() =>
            {
                PositionBeside(rect);
                _markerWindow?.Update(workspace);
            });
            workspace.WindowTracker.GameWindowPolled += rect => Dispatcher.UIThread.Post(() =>
            {
                PositionBeside(rect);
                _markerWindow?.Update(workspace);
            });
            workspace.WindowTracker.GameWindowLost += () => Dispatcher.UIThread.Post(() => _markerWindow?.Hide());
            _status.Text = canCapture ? workspace.ScannerStatus : CapturePermissionStatus;
            _tabs.ItemsSource = new[]
            {
                Tab("干员", CatalogViews.CreateOperators(workspace, this)),
                Tab("装备", CatalogViews.CreateEquipment(workspace, this)),
                Tab("追踪", TrackingView.Create(workspace, this)),
                Tab("阵容", LineupsView.Create(workspace, this)),
                Tab("设置", SourceSettingsView.Create(workspace, this))
            };
            UpdateSourceStatus(workspace);
            UpdateGameSummary(workspace);
            if (workspace.WindowTracker.CurrentGameRect is { } rect) PositionBeside(rect);
        }
        catch (Exception ex)
        {
            _status.Text = "无法启动：" + ex.Message;
        }
        finally { _loading = false; }
    }

    private static TabItem Tab(string title, Control content) => new()
    {
        Header = title, Content = content, FontSize = 12,
        Padding = new Thickness(8, 5)
    };

    private void UpdateSourceStatus(MacWorkspace workspace)
    {
        var target = workspace.GameSource.Current;
        var permission = MacScreenPermission.IsGranted ? "" : " · 待授权屏幕录制";
        _source.Text = target == null ? "正在寻找游戏窗口" : "已连接 " + target.Name + permission;
    }

    private async Task SaveTrackingAsync(MacWorkspace workspace)
    {
        try { await workspace.SaveTrackingAsync(); }
        catch (Exception ex) { Dispatcher.UIThread.Post(() => _status.Text = "追踪保存失败：" + ex.Message); }
    }

    private void UpdateGameSummary(MacWorkspace workspace)
    {
        var state = workspace.CurrentGameState;
        GameStateUpdater.UpdateShopTracking(state.ShopItems, workspace.Tracking.AllEntries.ToList());
        var hud = state.Hud;
        var found = state.ShopItems.Where(item => item.IsTracked).Select(item => item.Name).ToArray();
        _summary.Text = $"回合 {hud.Round?.ToString() ?? "—"}  ·  商店等级 {hud.ShopLevel?.ToString() ?? "—"}  ·  龙门币 {hud.Gold?.ToString() ?? "—"}"
            + (found.Length == 0 ? "" : "\n商店追踪：" + string.Join("、", found));
    }

    private void PositionBeside(RECT rect)
    {
        if (!FollowGameWindow || WindowState != WindowState.Normal) return;
        MacWindowInteraction.PositionBeside(this, rect.Left, rect.Top, rect.Right);
    }

    public async Task ShowMessageAsync(string title, string text)
    {
        var dialog = new Window
        {
            Title = title, Width = 400, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = MacPalette.Window,
            Content = new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(18), Foreground = MacPalette.Text } }
        };
        await dialog.ShowDialog(this);
    }
}
