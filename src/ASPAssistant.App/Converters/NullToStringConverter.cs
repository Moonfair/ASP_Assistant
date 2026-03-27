using System.Globalization;
using System.Windows.Data;

namespace ASPAssistant.App.Converters;

public class NullToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || (value is string s && s.Length == 0))
            return parameter?.ToString() ?? "全部";
        return value.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
