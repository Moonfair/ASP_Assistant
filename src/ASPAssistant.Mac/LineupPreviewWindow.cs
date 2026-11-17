using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac;

internal sealed class LineupPreviewWindow : Window
{
    public LineupPreviewWindow(Lineup lineup, MacWorkspace workspace)
    {
        Title = "阵容预览 · " + lineup.Name;
        Width = 700;
        Height = 700;
        MinWidth = 500;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 11, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = lineup.Name, FontSize = 22, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "作者：" + lineup.Author, Foreground = MacPalette.Secondary });
        if (lineup.CovenantTags is { Count: > 0 })
            panel.Children.Add(new TextBlock { Text = "盟约：" + string.Join("、", lineup.CovenantTags),
                TextWrapping = TextWrapping.Wrap });
        if (lineup.AvailableStrategies.Count > 0)
            panel.Children.Add(new TextBlock { Text = "策略：" + string.Join("、", lineup.AvailableStrategies),
                TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"干员（{lineup.Members.Count}）", FontSize = 16,
            FontWeight = FontWeight.Bold });
        foreach (var member in lineup.Members)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var op = workspace.Operators.FirstOrDefault(item => item.Name == member.OperatorName);
            row.Children.Add(AssetImages.Create(workspace.DataDirectory, op?.IconPath, 42));
            var labels = new StackPanel { Spacing = 3 };
            labels.Children.Add(new TextBlock { Text = member.OperatorName + (member.IsElite ? " · 精锐" : "") +
                (member.IsStackingCore ? " · 叠层核心" : "") +
                (member.IsDamageCore ? " · 输出核心" : "") });
            var gear = string.Join("、", member.EquipmentSlots.Where(item => !string.IsNullOrWhiteSpace(item)));
            if (gear.Length > 0) labels.Children.Add(new TextBlock { Text = gear, FontSize = 11,
                Foreground = MacPalette.Secondary, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(labels);
            panel.Children.Add(row);
        }
        if (!string.IsNullOrWhiteSpace(lineup.Description))
        {
            panel.Children.Add(new TextBlock { Text = "阵容介绍", FontSize = 16,
                FontWeight = FontWeight.Bold });
            panel.Children.Add(LineupDiagramView.Create(lineup.Description, workspace));
        }
        Content = new ScrollViewer { Content = panel };
    }
}
