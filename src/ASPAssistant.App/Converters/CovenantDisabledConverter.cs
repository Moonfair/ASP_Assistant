using System.Globalization;
using System.Windows.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Converters;

public sealed class CovenantDisabledConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values switch
    {
        [string { Length: > 0 } name, IReadOnlySet<string> enabled] => !enabled.Contains(name),
        [OperatorFilterTag { Kind: OperatorFilterKind.CoreCovenant, Value: string name }, CovenantAvailability state]
            => !state.EnabledCore.Contains(name),
        [OperatorFilterTag { Kind: OperatorFilterKind.OtherCovenant, Value: string name }, CovenantAvailability state]
            => !state.EnabledAdditional.Contains(name),
        _ => false
    };

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
