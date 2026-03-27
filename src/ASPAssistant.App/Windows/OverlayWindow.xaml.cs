using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class OverlayWindow : Window
{
    private const double MarkerSize = 28;

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

    public void ClearMarkers()
    {
        OverlayCanvas.Children.Clear();
    }

    public void UpdateMarkers(List<ShopItem> trackedShopItems, RECT gameClientRect)
    {
        OverlayCanvas.Children.Clear();

        foreach (var item in trackedShopItems)
        {
            if (!item.IsTracked)
                continue;

            var markerX = item.OcrRegion.X + item.OcrRegion.Width - MarkerSize / 2;
            var markerY = item.OcrRegion.Y - MarkerSize / 2;

            markerX = Math.Max(0, Math.Min(markerX, ActualWidth - MarkerSize));
            markerY = Math.Max(0, Math.Min(markerY, ActualHeight - MarkerSize));

            var marker = CreateMarker();
            Canvas.SetLeft(marker, markerX);
            Canvas.SetTop(marker, markerY);
            OverlayCanvas.Children.Add(marker);
        }
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
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(ellipse);
        grid.Children.Add(star);
        return grid;
    }
}
