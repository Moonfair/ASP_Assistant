using System.Diagnostics;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Mac;

internal static class LineupsView
{
    public static Control Create(MacWorkspace workspace, MainWindow owner)
    {
        var share = LineupShareService.FromCatalog(workspace.Operators, workspace.Equipment.Select(item => item.Name),
            workspace.OperatorCatalog);
        var heading = new StackPanel { Spacing = 7, Margin = new Thickness(2) };
        heading.Children.Add(new TextBlock { Text = "阵容库", FontSize = 17, FontWeight = FontWeight.Bold });
        var status = new TextBlock { Foreground = MacPalette.Secondary, TextWrapping = TextWrapping.Wrap };
        heading.Children.Add(status);
        var actions = new WrapPanel();
        var create = new Button { Content = "新建阵容" };
        create.Click += async (_, _) =>
        {
            var lineup = await LineupEditorWindow.EditAsync(owner, workspace, null);
            if (lineup != null) await workspace.Lineups.SaveAsync(lineup);
            status.Text = workspace.Lineups.Status;
        };
        actions.Children.Add(create);
        var import = new Button { Content = "从剪贴板导入" };
        import.Click += async (_, _) =>
        {
            try
            {
                var text = await owner.Clipboard?.GetTextAsync()! ?? "";
                string? link = null;
                if (LineupPublicLink.TryGetToken(text, out _))
                {
                    link = LineupPublicLink.Canonicalize(text);
                    status.Text = "正在读取阵容短链接…";
                    text = await LineupPublicLink.ResolveAsync(link);
                }
                var lineup = (await share.ImportAsync(text)) with { PublishedLink = link };
                await workspace.Lineups.SaveAsync(lineup);
                status.Text = workspace.Lineups.Status;
            }
            catch (Exception ex) { status.Text = "导入失败：" + ex.Message; }
        };
        actions.Children.Add(import);
        var account = new Button { Content = "账号" };
        account.Click += async (_, _) =>
        {
            await new LineupSiteAccountWindow(workspace.Site).ShowDialog<bool?>(owner);
            status.Text = workspace.Site.Email is { } address ? "已登录 " + address : "未登录卫戍阵容库";
        };
        actions.Children.Add(account);
        var history = new Button { Content = "卫戍战绩" };
        history.Click += async (_, _) => await new GarrisonHistoryWindow(workspace).ShowDialog(owner);
        actions.Children.Add(history);
        var site = new Button { Content = "打开作业站" };
        site.Click += (_, _) => Process.Start(new ProcessStartInfo("https://aspa.moonfair.cn/")
        { UseShellExecute = true });
        actions.Children.Add(site);
        heading.Children.Add(actions);
        var category = new ComboBox
        {
            ItemsSource = new[] { "全部阵容", "我的阵容", "导入阵容" }, SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Left, Width = 160
        };
        heading.Children.Add(category);

        var list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Lineup>((lineup, _) =>
            {
                var panel = new StackPanel { Spacing = 5 };
                panel.Children.Add(new TextBlock
                {
                    Text = $"{lineup.Name}  ·  {lineup.Author}", FontWeight = FontWeight.Bold, FontSize = 14
                });
                panel.Children.Add(new TextBlock
                {
                    Text = lineup.Summary, TextWrapping = TextWrapping.Wrap,
                    Foreground = MacPalette.Secondary, MaxHeight = 45
                });
                var buttons = new WrapPanel();
                var preview = new Button { Content = "预览" };
                preview.Click += async (_, _) =>
                    await new LineupPreviewWindow(lineup, workspace).ShowDialog(owner);
                buttons.Children.Add(preview);
                var edit = new Button { Content = "编辑" };
                edit.Click += async (_, _) =>
                {
                    var revised = await LineupEditorWindow.EditAsync(owner, workspace, lineup);
                    if (revised != null) await workspace.Lineups.SaveAsync(revised);
                    status.Text = workspace.Lineups.Status;
                };
                buttons.Children.Add(edit);
                var track = new Button { Content = "追踪" };
                track.Click += (_, _) =>
                {
                    var added = workspace.Tracking.TrackLineup(lineup);
                    status.Text = $"已加入 {added} 个追踪目标";
                };
                buttons.Children.Add(track);
                var export = new Button { Content = "生成短链接" };
                export.Click += async (_, _) =>
                {
                    try
                    {
                        export.IsEnabled = false;
                        status.Text = "正在生成阵容短链接…";
                        var link = await LineupPublicLink.CreateAsync(share.Export(lineup));
                        await owner.Clipboard!.SetTextAsync(link);
                        status.Text = "阵容短链接已复制，90 天内有效：" + link;
                    }
                    catch (Exception ex) { status.Text = ex.Message; }
                    finally { export.IsEnabled = true; }
                };
                buttons.Children.Add(export);
                var raw = new Button { Content = "复制文本" };
                raw.Click += async (_, _) =>
                {
                    try
                    {
                        await owner.Clipboard!.SetTextAsync(share.Export(lineup));
                        status.Text = "阵容分享文本已复制";
                    }
                    catch (Exception ex) { status.Text = ex.Message; }
                };
                buttons.Children.Add(raw);
                var upload = new Button { Content = "上传作业站" };
                upload.Click += async (_, _) =>
                {
                    try
                    {
                        if (workspace.Site.Email is null)
                            await new LineupSiteAccountWindow(workspace.Site).ShowDialog<bool?>(owner);
                        if (workspace.Site.Email is null) return;
                        upload.IsEnabled = false;
                        status.Text = "正在上传阵容…";
                        var link = await workspace.Site.UploadAsync(lineup.Id, share.Export(lineup));
                        await owner.Clipboard!.SetTextAsync(link.ToString());
                        status.Text = "上传成功，链接已复制：" + link;
                    }
                    catch (Exception ex) { status.Text = "上传失败：" + ex.Message; }
                    finally { upload.IsEnabled = true; }
                };
                buttons.Children.Add(upload);
                var delete = new Button { Content = "删除" };
                delete.Click += async (_, _) =>
                {
                    var confirm = new Window
                    {
                        Title = "删除阵容", Width = 380, Height = 180,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    var question = new StackPanel { Spacing = 14, Margin = new Thickness(18) };
                    question.Children.Add(new TextBlock
                    {
                        Text = $"确定删除「{lineup.Name}」吗？", TextWrapping = TextWrapping.Wrap
                    });
                    var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    var cancel = new Button { Content = "取消" };
                    cancel.Click += (_, _) => confirm.Close(false);
                    choices.Children.Add(cancel);
                    var accept = new Button { Content = "删除阵容" };
                    accept.Click += (_, _) => confirm.Close(true);
                    choices.Children.Add(accept);
                    question.Children.Add(choices);
                    confirm.Content = question;
                    if (!await confirm.ShowDialog<bool>(owner)) return;
                    await workspace.Lineups.DeleteAsync(lineup);
                    status.Text = workspace.Lineups.Status;
                };
                buttons.Children.Add(delete);
                panel.Children.Add(buttons);
                return new Border
                {
                    Background = MacPalette.Surface, BorderBrush = MacPalette.Border,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 5), Child = panel
                };
            })
        };
        MacListStyling.Apply(list);
        void Refresh() => list.ItemsSource = workspace.Lineups.Lineups
            .Where(lineup => category.SelectedIndex switch
            {
                1 => !lineup.IsImported,
                2 => lineup.IsImported,
                _ => true
            }).ToArray();
        category.SelectionChanged += (_, _) => Refresh();
        ((INotifyCollectionChanged)workspace.Lineups.Lineups).CollectionChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Refresh();
        DockPanel.SetDock(heading, Dock.Top);
        return new DockPanel { Children = { heading, list } };
    }
}
