using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

public partial class LineupPreviewView : UserControl
{
    public LineupPreviewView() => InitializeComponent();

    internal void ShowLineup(Lineup lineup, Func<bool, string, string?> findIcon)
    {
        LineupTitle.Text = lineup.Name;
        AuthorLabel.Text = $"作者：{lineup.Author}";
        MemberStrip.DataContext = lineup.Members.Select(member => new PreviewMember(
            member.OperatorName, member.IsStackingCore, member.IsDamageCore, member.Equipment1, member.Equipment2,
            findIcon(false, member.OperatorName), findIcon(true, member.Equipment1), findIcon(true, member.Equipment2))).ToArray();
        Introduction.Document = LineupMarkdown.Render(lineup.Description, findIcon, "作者尚未填写阵容介绍。");
    }

    private sealed record PreviewMember(string OperatorName, bool IsStackingCore, bool IsDamageCore,
        string Equipment1, string Equipment2, string? IconPath, string? Equipment1IconPath, string? Equipment2IconPath);
}
