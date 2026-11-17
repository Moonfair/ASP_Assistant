using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac;

internal static class TrackingView
{
    public static Control Create(MacWorkspace workspace, MainWindow owner)
    {
        var heading = new StackPanel { Spacing = 8, Margin = new Thickness(2) };
        heading.Children.Add(new TextBlock
        {
            Text = "本局追踪", FontSize = 17, FontWeight = FontWeight.Bold
        });
        var count = new TextBlock { Foreground = MacPalette.Secondary, FontSize = 11 };
        heading.Children.Add(count);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var scan = new Button { Content = "截屏录入" };
        scan.Click += (_, _) => workspace.ScanNow();
        buttons.Children.Add(scan);
        var clearBan = new Button { Content = "清空禁用" };
        clearBan.Click += (_, _) =>
        {
            workspace.ClearBans();
            workspace.Bans.ClearBans();
        };
        buttons.Children.Add(clearBan);
        var clear = new Button { Content = "清空追踪" };
        clear.Click += (_, _) => workspace.Tracking.ClearTracking();
        buttons.Children.Add(clear);
        heading.Children.Add(buttons);

        var list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<TrackingEntry>((entry, _) =>
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("38,*,Auto") };
                row.Children.Add(AssetImages.Create(workspace.DataDirectory, entry.IconPath, 34));
                var label = new TextBlock
                {
                    Text = entry.Name + (entry.Type == TrackingType.Operator ? " · 干员" : " · 装备"),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0),
                    FontSize = 12, Foreground = MacPalette.Text
                };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var remove = new Button { Content = "移除", FontSize = 11, Padding = new Thickness(8, 4) };
                remove.Click += (_, _) => workspace.Tracking.RemoveTracking(entry.Name);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                return new Border
                {
                    Background = MacPalette.Surface, BorderBrush = MacPalette.Border,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 4), Child = row
                };
            })
        };
        MacListStyling.Apply(list);
        void Refresh()
        {
            var entries = workspace.Tracking.AllEntries.ToList();
            list.ItemsSource = entries;
            count.Text = $"干员 {workspace.Tracking.TrackedOperators.Count} · 装备 {workspace.Tracking.TrackedEquipment.Count}";
        }
        workspace.Tracking.TrackingChanged += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
        DockPanel.SetDock(heading, Dock.Top);
        return new DockPanel { Children = { heading, list } };
    }
}
