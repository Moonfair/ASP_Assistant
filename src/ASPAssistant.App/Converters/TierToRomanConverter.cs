using System.Globalization;
using System.Windows.Data;

namespace ASPAssistant.App.Converters;

public class TierToRomanConverter : IValueConverter
{
    private static readonly string[] RomanNumerals = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int tier && tier >= 1 && tier <= 6)
            return RomanNumerals[tier];
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
