using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac;

internal static class CatalogDetailWindow
{
    public static Task ShowOperatorAsync(Window owner, Operator op, MacWorkspace workspace)
    {
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        header.Children.Add(AssetImages.Create(workspace.DataDirectory, op.IconPath, 84));
        header.Children.Add(new TextBlock
        {
            Text = $"{op.Name}\n{op.TierLabel}阶 · {string.Join(" / ", op.CoreCovenants)}",
            FontSize = 20, FontWeight = FontWeight.Bold
        });
        content.Children.Add(header);
        AddSection(content, "附加盟约", string.Join("、", op.AdditionalCovenants));
        AddSection(content, "普通效果", string.Join("\n", op.Normal.Traits.Select(t => $"{t.TraitType}：{t.TraitDescription}")));
        AddSection(content, "精锐效果", string.Join("\n", op.Elite.Traits.Select(t => $"{t.TraitType}：{t.TraitDescription}")));
        if (workspace.CatalogDetails.Skills.TryGetValue(op.Name, out var skills))
            foreach (var skill in skills.OrderBy(skill => skill.Slot))
                AddSection(content, skill.Name, skill.Description + "\n" + string.Join(" · ", skill.Tags));
        if (workspace.CatalogDetails.Tips?.Operators.GetValueOrDefault(op.Name) is { } tip)
            AddSection(content, "实战小知识", tip.Text + $"\n来源：{workspace.CatalogDetails.Tips.Title}");
        return Show(owner, op.Name, content, dialog => VotingView.Create(dialog, op, workspace));
    }

    public static Task ShowEquipmentAsync(Window owner, Equipment item, MacWorkspace workspace)
    {
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        header.Children.Add(AssetImages.Create(workspace.DataDirectory, item.IconPath, 84));
        header.Children.Add(new TextBlock
        {
            Text = $"{item.Name}\n{item.Tier}阶 · {item.Type}",
            FontSize = 20, FontWeight = FontWeight.Bold
        });
        content.Children.Add(header);
        AddSection(content, "普通效果", item.Normal.EffectDescription);
        AddSection(content, "精锐效果", item.Elite.EffectDescription);
        if (!string.IsNullOrWhiteSpace(item.FusionFormula)) AddSection(content, "合成配方", item.FusionFormula);
        if (workspace.CatalogDetails.Tips?.Equipment.GetValueOrDefault(item.Name) is { } tip)
            AddSection(content, "实战小知识", tip.Text + $"\n来源：{workspace.CatalogDetails.Tips.Title}");
        return Show(owner, item.Name, content);
    }

    private static void AddSection(StackPanel panel, string title, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = MacPalette.Secondary });
    }

    private static Task Show(Window owner, string title, StackPanel content,
        Func<Window, Control>? extra = null)
    {
        var window = new Window
        {
            Title = title, Width = 560, Height = 690, MinWidth = 420, MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = content }
        };
        if (extra != null) content.Children.Add(extra(window));
        return window.ShowDialog(owner);
    }
}
