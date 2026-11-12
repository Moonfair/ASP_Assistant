using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Converters;

public sealed class LineupActiveCovenantsConverter : IMultiValueConverter
{
    internal LineupCovenantCalculator? Calculator { get; set; }

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [Lineup lineup, var state] || Calculator is null) return null;
        var names = Calculator.GetActiveCovenants(lineup);
        var availability = state as CovenantAvailability;
        var summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            ToolTip = "按阵容中列出的干员、精锐状态和装备计算。删除线表示本局禁用。"
        };
        summary.Inlines.Add(new Run(names.Count == 0 ? "已激活盟约：无" : "已激活盟约："));
        for (var i = 0; i < names.Count; i++)
        {
            if (i > 0) summary.Inlines.Add(new Run(" · "));
            var name = names[i];
            var run = new Run(name);
            if (availability is not null && !availability.EnabledCore.Contains(name)
                && !availability.EnabledAdditional.Contains(name))
            {
                run.TextDecorations = TextDecorations.Strikethrough;
                run.Foreground = Brushes.LightCoral;
            }
            summary.Inlines.Add(run);
        }
        return summary;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
