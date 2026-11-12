using System.Globalization;
using System.Windows.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Converters;

public sealed class LineupActiveCovenantsConverter : IValueConverter
{
    internal LineupCovenantCalculator? Calculator { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Lineup lineup || Calculator is null) return "";
        var names = Calculator.GetActiveCovenants(lineup);
        return names.Count == 0 ? "已激活盟约：无" : "已激活盟约：" + string.Join(" · ", names);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
