using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Mac;

internal static class SourceSettingsView
{
    public static Control Create(MacWorkspace workspace, MainWindow owner)
    {
        var panel = new StackPanel { Spacing = 9, Margin = new Thickness(2) };
        panel.Children.Add(new TextBlock { Text = "游戏来源", FontSize = 17, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "选择运行游戏的窗口和本机 ADB 设备。首次截图时，macOS 可能要求授予屏幕录制权限。",
            TextWrapping = TextWrapping.Wrap, Foreground = MacPalette.Secondary
        });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = MacPalette.Secondary };

        var adbPath = new TextBox { Watermark = "ADB 程序路径", Text = MacAdbClient.FindExecutable() ?? "" };
        panel.Children.Add(adbPath);
        var devices = new ComboBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Labeled("ADB 设备", devices));
        var windows = new ComboBox
        {
            Width = 340,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemTemplate = new FuncDataTemplate<HostWindow>((item, _) => new TextBlock { Text = item.Label })
        };
        panel.Children.Add(Labeled("游戏窗口", windows));
        var actions = new WrapPanel();
        var refresh = new Button { Content = "刷新来源" };
        refresh.Click += async (_, _) => await RefreshAsync();
        actions.Children.Add(refresh);
        var screenPermission = new Button { Content = "屏幕录制权限" };
        screenPermission.Click += (_, _) =>
        {
            if (MacScreenPermission.IsGranted || MacScreenPermission.Request())
                status.Text = "屏幕录制已授权。若刚授予权限，请重新打开应用。";
            else status.Text = "请在系统设置 → 隐私与安全性 → 屏幕录制中允许 ASP Assistant，然后重新打开应用。";
        };
        actions.Children.Add(screenPermission);
        var connect = new Button { Content = "连接 ADB" };
        connect.Click += (_, _) =>
        {
            try
            {
                if (windows.SelectedItem is not HostWindow window || devices.SelectedItem is not string serial)
                    throw new InvalidOperationException("请选择游戏窗口和 ADB 设备");
                workspace.SelectAdbSource(window, adbPath.Text?.Trim() ?? "", serial);
                status.Text = $"已连接 {window.Label} · {serial}";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        actions.Children.Add(connect);
        var native = new Button { Content = "连接原生窗口" };
        native.Click += (_, _) =>
        {
            if (!MacScreenPermission.IsGranted)
            {
                MacScreenPermission.Request();
                status.Text = "请先授权屏幕录制并重新打开应用，再连接原生窗口。";
                return;
            }
            var target = GameTargetSelection.FindWindowsClient();
            if (target == null) { status.Text = "未发现明日方舟窗口"; return; }
            workspace.SelectNativeSource(target);
            status.Text = "已连接 " + target.Name;
        };
        actions.Children.Add(native);
        var automatic = new Button { Content = "自动检测" };
        automatic.Click += (_, _) =>
        {
            workspace.SelectAutomaticSource();
            status.Text = workspace.GameSource.Current is { } target
                ? "已自动连接 " + target.Name : "自动检测已开启，等待游戏窗口";
        };
        actions.Children.Add(automatic);
        var scan = new Button { Content = "立即截屏识别" };
        scan.Click += (_, _) =>
        {
            if (workspace.Capture == null)
                status.Text = "请先授权屏幕录制并重启应用，再执行截图识别。";
            else workspace.ScanNow();
        };
        actions.Children.Add(scan);
        panel.Children.Add(actions);
        panel.Children.Add(status);

        panel.Children.Add(new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8) });
        panel.Children.Add(new TextBlock { Text = "应用", FontSize = 17, FontWeight = FontWeight.Bold });
        var topmost = new CheckBox { Content = "窗口置顶", IsChecked = owner.Topmost };
        topmost.IsCheckedChanged += (_, _) => owner.Topmost = topmost.IsChecked == true;
        panel.Children.Add(topmost);
        var follow = new CheckBox { Content = "跟随游戏窗口", IsChecked = owner.FollowGameWindow };
        follow.IsCheckedChanged += (_, _) => owner.SetFollowGameWindow(follow.IsChecked == true);
        panel.Children.Add(follow);
        var tools = new WrapPanel();
        var openData = new Button { Content = "打开用户数据" };
        openData.Click += (_, _) => Process.Start(new ProcessStartInfo("open")
        { ArgumentList = { workspace.SettingsDirectory }, UseShellExecute = false });
        tools.Children.Add(openData);
        var openSite = new Button { Content = "卫戍阵容库" };
        openSite.Click += (_, _) => Process.Start(new ProcessStartInfo("https://aspa.moonfair.cn/")
        { UseShellExecute = true });
        tools.Children.Add(openSite);
        var updates = new MacUpdateService();
        var releaseStatus = new TextBlock { Text = "当前版本：" + updates.CurrentVersion.ToString(3),
            TextWrapping = TextWrapping.Wrap, Foreground = MacPalette.Secondary };
        var releases = new Button { Content = "检查更新" };
        releases.Click += async (_, _) => await CheckUpdatesAsync();
        tools.Children.Add(releases);
        var releasePage = new Button { Content = "打开发布页" };
        releasePage.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/Moonfair/ASP_Assistant/releases")
        { UseShellExecute = true });
        tools.Children.Add(releasePage);
        var feedback = new Button { Content = "发送日志反馈" };
        feedback.Click += async (_, _) =>
        {
            feedback.IsEnabled = false;
            status.Text = "正在发送反馈…";
            try { status.Text = "反馈已发送，编号：" + await workspace.Site.SendFeedbackAsync(); }
            catch (Exception ex) { status.Text = "发送反馈失败：" + ex.Message; }
            finally { feedback.IsEnabled = true; }
        };
        tools.Children.Add(feedback);
        panel.Children.Add(tools);
        panel.Children.Add(releaseStatus);
        panel.Children.Add(new TextBlock
        {
            Text = $"数据目录：{workspace.DataDirectory}\n设置目录：{workspace.SettingsDirectory}",
            Foreground = MacPalette.Secondary, TextWrapping = TextWrapping.Wrap, FontSize = 11
        });

        async Task RefreshAsync()
        {
            var candidates = GameTargetSelection.ListWindows()
                .Where(window => window.ProcessId != Environment.ProcessId).ToArray();
            windows.ItemsSource = candidates;
            var currentHandle = workspace.GameSource.Current?.Window ?? 0;
            windows.SelectedItem = candidates.FirstOrDefault(window => window.Handle == currentHandle)
                ?? candidates.FirstOrDefault(window => window.Title == "明日方舟")
                ?? candidates.FirstOrDefault();
            var path = adbPath.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                status.Text = "未找到 ADB。请安装 Android platform-tools，并填写 adb 路径。";
                devices.ItemsSource = Array.Empty<string>();
                return;
            }
            try
            {
                var found = await new MacAdbClient(path).ListDevicesAsync();
                devices.ItemsSource = found;
                if (found.Count > 0) devices.SelectedIndex = 0;
                status.Text = $"发现 {found.Count} 个在线 ADB 设备";
            }
            catch (Exception ex) { status.Text = "ADB 查询失败：" + ex.Message; }
        }
        async Task CheckUpdatesAsync()
        {
            releases.IsEnabled = false;
            releaseStatus.Text = "正在检查 macOS 更新…";
            try
            {
                var update = await updates.CheckAsync();
                releaseStatus.Text = update is null
                    ? "当前没有可用的 macOS 更新。"
                    : $"发现 macOS 新版本 {update.Tag}。请打开发布页下载安装。";
            }
            catch (Exception ex) { releaseStatus.Text = "检查更新失败：" + ex.Message; }
            finally { releases.IsEnabled = true; }
        }
        _ = RefreshAsync();
        _ = CheckUpdatesAsync();
        return new ScrollViewer { Content = new Border
        {
            Background = MacPalette.Surface, BorderBrush = MacPalette.Border,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12), Child = panel
        } };
    }

    private static Control Labeled(string label, Control input)
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(new TextBlock { Text = label, Foreground = MacPalette.Secondary });
        panel.Children.Add(input);
        return panel;
    }
}
