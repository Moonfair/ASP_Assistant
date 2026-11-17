using Avalonia.Media;

namespace ASPAssistant.Mac;

internal static class MacPalette
{
    public static readonly SolidColorBrush Window = new();
    public static readonly SolidColorBrush Toolbar = new();
    public static readonly SolidColorBrush Surface = new();
    public static readonly SolidColorBrush Text = new();
    public static readonly SolidColorBrush Secondary = new();
    public static readonly SolidColorBrush Border = new();
    public static readonly SolidColorBrush Accent = new();
    public static readonly SolidColorBrush AccentText = new();

    public static void Apply(bool dark)
    {
        Window.Color = Color.Parse(dark ? "#17191D" : "#F5F5F7");
        Toolbar.Color = Color.Parse(dark ? "#22252B" : "#ECEEF2");
        Surface.Color = Color.Parse(dark ? "#292D34" : "#FFFFFF");
        Text.Color = Color.Parse(dark ? "#F5F5F7" : "#1D1D1F");
        Secondary.Color = Color.Parse(dark ? "#AEB4BD" : "#666B73");
        Border.Color = Color.Parse(dark ? "#3B414B" : "#D9DCE2");
        Accent.Color = Color.Parse(dark ? "#0A84FF" : "#007AFF");
        AccentText.Color = Colors.White;
    }
}
