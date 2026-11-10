using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Controls;

/// <summary>Uses the original transparent icon as a mask, inheriting the chip's text color.</summary>
public sealed class CovenantIcon : FrameworkElement
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();
    private ImageBrush? _mask;
    public static readonly DependencyProperty CovenantProperty = DependencyProperty.Register(
        nameof(Covenant), typeof(object), typeof(CovenantIcon), new PropertyMetadata(null, OnCovenantChanged));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(CovenantIcon), new FrameworkPropertyMetadata(Brushes.White,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public object? Covenant { get => GetValue(CovenantProperty); set => SetValue(CovenantProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public CovenantIcon() => Visibility = Visibility.Collapsed;

    private static void OnCovenantChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (CovenantIcon)d;
        string? name = e.NewValue switch
        {
            string text => text,
            OperatorFilterTag { Kind: OperatorFilterKind.CoreCovenant or OperatorFilterKind.OtherCovenant } tag => tag.Value as string,
            _ => null
        };
        icon._mask = null;
        if (!string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            try
            {
                if (!Cache.TryGetValue(name, out var image))
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "data", "icons", "covenants", name + ".png");
                    if (File.Exists(path))
                    {
                        using var stream = File.OpenRead(path);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        Cache[name] = image = bitmap;
                    }
                }
                if (image != null)
                {
                    icon._mask = new ImageBrush(image) { Stretch = Stretch.Uniform };
                    icon._mask.Freeze();
                }
            }
            catch (Exception ex) when (ex is IOException or FileFormatException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            { /* Missing/broken assets leave a readable text-only label. */ }
        }
        icon.Visibility = icon._mask == null ? Visibility.Collapsed : Visibility.Visible;
        icon.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_mask == null) return;
        drawingContext.PushOpacityMask(_mask);
        drawingContext.DrawRectangle(Foreground, null, new Rect(RenderSize));
        drawingContext.Pop();
    }
}
