using System.Globalization;
using System.Windows.Data;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Converters;

public sealed class LineupMoveEnabledConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [int index, int count, LineupMove move] || index >= count) return false;

        return move switch
        {
            LineupMove.Up or LineupMove.Top => index > 0,
            LineupMove.Down => index < count - 1,
            _ => false
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
