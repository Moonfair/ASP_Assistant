using System.Globalization;
using System.Windows.Data;

namespace ASPAssistant.App.Converters;

public sealed class StrategyCardPortraitsConverter : IValueConverter
{
    internal Func<string, string?>? FindIcon { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> { Count: > 0 } strategies) return Array.Empty<Portrait>();

        const int visibleCount = 5;
        var portraits = strategies.Take(visibleCount)
            .Select(name => new Portrait(name, name, FindIcon?.Invoke(name) ?? "")).ToList();
        if (strategies.Count > visibleCount)
        {
            var remaining = strategies.Count - visibleCount;
            portraits.Add(new($"+{remaining}", $"另有 {remaining} 项策略", ""));
        }
        return portraits;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private sealed record Portrait(string Text, string Label, string IconPath);
}
