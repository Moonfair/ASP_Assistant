using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class CatalogDetailWindow : Window
{
    private readonly CatalogDetails _details;
    private readonly Operator? _operator;
    private readonly LineupSiteClient? _site;
    private readonly IReadOnlyList<OperatorSkill> _skills = [];
    private Uri? _tipLink;

    internal CatalogDetailWindow(Operator op, CatalogDetails details, IReadOnlyList<Equipment> equipment,
        LineupSiteClient? site)
    {
        InitializeComponent();
        _operator = op;
        _details = details;
        _site = site;
        _skills = details.Skills.TryGetValue(op.Name, out var skills) ? skills : [];
        SetHeader(op.Name, op.IconPath, op.Tier,
            op.StrategyOnly ? "策略干员" : $"{op.EquipmentSlotCount} 个装备槽");

        foreach (var name in op.CoreCovenants) AddTag(name, core: true);
        foreach (var name in op.AdditionalCovenants) AddTag(name, core: false);
        ShowTip(details.Tips?.Operators, op.Name);
        AddTraits("普通", op.Normal.Traits);
        if (op.Elite.Traits.Count > 0) AddTraits("精锐", op.Elite.Traits);

        if (_skills.Count > 0)
        {
            SkillsTab.Visibility = Visibility.Visible;
            SkillDetails.ItemsSource = _skills;
        }
        if (site is not null && details.Season is not null)
            InitializeVoting(equipment);
    }

    internal CatalogDetailWindow(Equipment equipment, CatalogDetails details)
    {
        InitializeComponent();
        _details = details;
        SetHeader(equipment.Name, equipment.IconPath, equipment.Tier, equipment.Type switch
        {
            EquipmentType.JobChange => "转职装备",
            EquipmentType.Exclusive => "专属装备",
            _ => "装备"
        });
        ShowTip(details.Tips?.Equipment, equipment.Name);
        AddSection("普通效果", Body(EffectOrEmpty(equipment.Normal.EffectDescription)));
        if (!string.IsNullOrWhiteSpace(equipment.Elite.EffectDescription))
            AddSection("精锐效果", Body(equipment.Elite.EffectDescription));
        if (!string.IsNullOrWhiteSpace(equipment.FusionFormula))
            AddSection("合成配方", Body(equipment.FusionFormula));
    }

    private void SetHeader(string name, string iconPath, int tier, string category)
    {
        Title = name + " · 详情";
        TitleText.Text = name;
        SummaryText.Text = tier > 0 ? $"{tier} 阶　{category}" : $"特殊　{category}";
        DeferredIcon.SetPath(Portrait, iconPath);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 30);
    }

    private void AddTag(string text, bool core)
    {
        Tags.Visibility = Visibility.Visible;
        var label = Body(text);
        label.Foreground = (Brush)FindResource(core ? "AccentBrush" : "TextSecondaryBrush");
        Tags.Children.Add(new Border
        {
            Background = new SolidColorBrush(core ? Color.FromArgb(34, 79, 195, 247) : Color.FromArgb(20, 255, 255, 255)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = core ? "核心盟约" : "附加盟约",
            Child = label
        });
    }

    private void ShowTip(Dictionary<string, CatalogTip>? entries, string name)
    {
        if (_details.Tips is not { } source || entries is null || !entries.TryGetValue(name, out var tip)) return;
        TipCard.Visibility = Visibility.Visible;
        TipText.Text = tip.Text;
        TipSource.Content = Body($"{source.Title} · {TimeSpan.FromSeconds(tip.Seconds):mm\\:ss}");
        if (Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host == "www.bilibili.com")
            _tipLink = new UriBuilder(uri) { Query = $"t={tip.Seconds}" }.Uri;
        TipSource.Visibility = _tipLink is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenTipSource(object sender, RoutedEventArgs e)
    {
        if (_tipLink is null) return;
        try { Process.Start(new ProcessStartInfo(_tipLink.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception) { TipSource.ToolTip = "无法打开浏览器"; }
    }

    private void AddTraits(string title, IReadOnlyList<TraitEntry> traits)
    {
        var panel = new StackPanel();
        foreach (var trait in traits)
        {
            panel.Children.Add(new TextBlock
            {
                Text = trait.TraitType,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush")
            });
            var description = Body(trait.TraitDescription);
            description.Margin = new Thickness(0, 3, 0, 8);
            panel.Children.Add(description);
        }
        if (traits.Count == 0) panel.Children.Add(Body("暂无效果说明"));
        AddSection(title, panel);
    }

    private void AddSection(string heading, UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = heading, Style = (Style)FindResource("DetailHeading") });
        panel.Children.Add(content);
        Effects.Children.Add(new Border { Style = (Style)FindResource("DetailSection"), Child = panel });
    }

    private static TextBlock Body(string text) => new()
    {
        Text = text.Replace(@"\r\n", "\n").Replace(@"\n", "\n"),
        TextWrapping = TextWrapping.Wrap
    };

    private static string EffectOrEmpty(string text) => string.IsNullOrWhiteSpace(text) ? "暂无效果说明" : text;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
