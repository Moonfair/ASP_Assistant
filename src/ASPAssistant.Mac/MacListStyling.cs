using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace ASPAssistant.Mac;

internal static class MacListStyling
{
    public static void Apply(ListBox list)
    {
        var itemStyle = new Style(x => x.OfType<ListBoxItem>());
        itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.MinHeightProperty, 0d));
        list.Styles.Add(itemStyle);
    }
}
