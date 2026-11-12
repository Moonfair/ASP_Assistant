using System.Globalization;
using System.Windows.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Converters;

public sealed class LineupTrackingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values is [Lineup lineup, TrackingViewModel tracking, int] && tracking.IsLineupTracked(lineup);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
