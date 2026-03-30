using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class OverlayWindow : Window
{
    private const double MarkerSize = 27;

    // Persistent marker elements keyed by ShopItem.Id.
    // Using Id (not Name) lets multiple cards with the same operator name each
    // have their own independent marker.
    private readonly Dictionary<string, FrameworkElement> _markerPool = [];

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
            exStyle | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW);
    }

    public void UpdatePosition(RECT gameClientRect)
    {
        Left = gameClientRect.Left;
        Top = gameClientRect.Top;
        Width = gameClientRect.Width;
        Height = gameClientRect.Height;
    }

    /// <summary>
    /// Synchronises the overlay with the current shop state.
    /// New tracked items fade in; existing ones are repositioned without
    /// recreation; items no longer tracked fade out and are released.
    /// </summary>
    public void UpdateShopItems(IReadOnlyList<ShopItem> shopItems)
    {
        var nowTracked = shopItems
            .Where(s => s.IsTracked)
            .ToDictionary(s => s.Id);

        // Fade out markers whose card slots are no longer present
        foreach (var id in _markerPool.Keys.Except(nowTracked.Keys).ToList())
            FadeOutAndRemove(id);

        // Add new markers or reposition existing ones
        foreach (var item in nowTracked.Values)
        {
            var (mx, my) = ComputeMarkerPosition(item);

            if (_markerPool.TryGetValue(item.Id, out var existing))
            {
                Canvas.SetLeft(existing, mx);
                Canvas.SetTop(existing, my);
            }
            else
            {
                var marker = CreateMarker();
                marker.Opacity = 0;
                Canvas.SetLeft(marker, mx);
                Canvas.SetTop(marker, my);
                OverlayCanvas.Children.Add(marker);
                _markerPool[item.Id] = marker;

                marker.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            }
        }
    }

    private void FadeOutAndRemove(string id)
    {
        var el = _markerPool[id];
        _markerPool.Remove(id);

        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
        anim.Completed += (_, _) =>
        {
            OverlayCanvas.Children.Remove(el);
        };
        el.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>
    /// Returns the canvas pixel position for a marker placed at the top-right
    /// corner of the operator card. OcrRegion stores the card's normalised
    /// bounding box [0,1] (X, Y, Width, Height).
    /// </summary>
    private (double X, double Y) ComputeMarkerPosition(ShopItem item)
    {
        // Place marker just inside the card's top-right corner.
        var mx = (item.OcrRegion.X + item.OcrRegion.Width) * ActualWidth - MarkerSize - 2;
        var my = item.OcrRegion.Y * ActualHeight + 2;

        mx = Math.Max(0, Math.Min(mx, ActualWidth - MarkerSize));
        my = Math.Max(0, Math.Min(my, ActualHeight - MarkerSize));

        return (mx, my);
    }

    private static Grid CreateMarker()
    {
        var grid = new Grid
        {
            Width = MarkerSize,
            Height = MarkerSize
        };

        var ellipse = new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                ShadowDepth = 2,
                Opacity = 0.4
            }
        };

        var star = new TextBlock
        {
            Text = "★",
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(ellipse);
        grid.Children.Add(star);
        return grid;
    }
}
