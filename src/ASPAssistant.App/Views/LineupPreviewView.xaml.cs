using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

public partial class LineupPreviewView : UserControl
{
    private const double DiagramAsideMinimumWidth = 740;
    private const string EmptyIntroduction = "作者尚未填写阵容介绍。";
    private Action<bool>? _renderDescription;
    private bool _hasDiagrams;
    private bool _showingDiagramsAside;

    public LineupPreviewView() => InitializeComponent();

    internal void ShowLineup(Lineup lineup, Func<bool, string, string?> findIcon, Func<string, string?>? findStrategyIcon = null)
    {
        LineupTitle.Text = lineup.Name;
        AuthorLabel.Text = $"作者：{lineup.Author}";
        CovenantTags.Text = lineup.CovenantTagSummary;
        MemberStrip.DataContext = lineup.Members.Select(member => new PreviewMember(
            member.OperatorName, member.IsStackingCore, member.IsDamageCore, findIcon(false, member.OperatorName),
            member.EquipmentSlots.Take(member.Equipment3 is not null || member.Equipment4 is not null ? 4 : 2)
                .Select(name => new PreviewSlot(name, findIcon(true, name))).ToArray())).ToArray();
        _hasDiagrams = LineupDiagramSource.FindBlocks(lineup.Description, out _).Count > 0;
        _renderDescription = aside => RenderDescription(lineup, findIcon, findStrategyIcon, aside);
        _renderDescription(ShowDiagramsAside(ActualWidth));
    }

    private bool ShowDiagramsAside(double width) => _hasDiagrams && width >= DiagramAsideMinimumWidth;

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var showAside = ShowDiagramsAside(e.NewSize.Width);
        if (e.WidthChanged && showAside != _showingDiagramsAside) _renderDescription?.Invoke(showAside);
    }

    private void RenderDescription(Lineup lineup, Func<bool, string, string?> findIcon,
        Func<string, string?>? findStrategyIcon, bool showDiagramsAside)
    {
        _showingDiagramsAside = showDiagramsAside;
        DiagramColumn.Width = showDiagramsAside ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        DiagramPreviewHost.Visibility = showDiagramsAside ? Visibility.Visible : Visibility.Collapsed;
        if (showDiagramsAside)
        {
            var (introduction, diagrams) = LineupMarkdown.RenderWithDiagramsAside(lineup.Description,
                findIcon, EmptyIntroduction, findStrategyIcon, lineup.AvailableStrategies);
            Introduction.Document = introduction;
            DiagramPreview.Document = diagrams;
        }
        else
        {
            Introduction.Document = LineupMarkdown.Render(lineup.Description, findIcon, EmptyIntroduction,
                findStrategyIcon, lineup.AvailableStrategies);
            DiagramPreview.Document = null;
        }
    }

    private sealed record PreviewMember(string OperatorName, bool IsStackingCore, bool IsDamageCore,
        string? IconPath, IReadOnlyList<PreviewSlot> EquipmentSlots);
    private sealed record PreviewSlot(string Name, string? IconPath);
}
