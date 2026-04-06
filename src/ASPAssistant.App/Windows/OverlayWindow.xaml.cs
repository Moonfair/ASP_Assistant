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
    private const double BanMarkerSize = 30;

    // Persistent marker elements keyed by ShopItem.Id.
    // Using Id (not Name) lets multiple cards with the same operator name each
    // have their own independent marker.
    private readonly Dictionary<string, FrameworkElement> _markerPool = [];

    // Ban-screen avatar markers keyed by operator name.
    private readonly List<FrameworkElement> _banMarkerPool = [];

    // The full-screen instruction layer shown at the start of ban detection.
    private FrameworkElement? _banInstructionLayer;

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
            Text = "\u2605",   // ?
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

    // ?? Ban instruction overlay ???????????????????????????????????????????????

    /// <summary>
    /// Displays a semi-transparent grey overlay on the game window with an
    /// animated downward arrow prompting the player to scroll. The overlay
    /// intercepts the first scroll wheel event, dismisses itself, and
    /// forwards the scroll to the game window so the player's input is not lost.
    /// </summary>
    public void ShowBanInstructionOverlay()
    {
        // Remove any pre-existing layer (e.g. rapid re-entry).
        HideBanInstructionOverlay();

        // Full-screen dimming rectangle.
        var dimRect = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(dimRect, 0);
        Canvas.SetTop(dimRect, 0);
        dimRect.Width = ActualWidth > 0 ? ActualWidth : 1280;
        dimRect.Height = ActualHeight > 0 ? ActualHeight : 720;

        // Central message panel.
        var text = new TextBlock
        {
            // "???????????"
            Text = "\u5411\u4e0b\u6eda\u52a8\u4ee5\u4fdd\u5b58\u7981\u7528\u5e72\u5458",
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.8
            }
        };

        // Downward-pointing arrow drawn as a Polygon, animated to bounce vertically.
        var arrowTransform = new TranslateTransform();
        var arrowAnim = new DoubleAnimation(0, 12,
            new Duration(TimeSpan.FromSeconds(0.6)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        arrowTransform.BeginAnimation(TranslateTransform.YProperty, arrowAnim);

        var arrow = new Polygon
        {
            Points = new PointCollection { new(0, 0), new(28, 0), new(14, 18) },
            Fill = Brushes.White,
            RenderTransform = arrowTransform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 0.6
            }
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(text);
        panel.Children.Add(new Border { Height = 12 });
        panel.Children.Add(arrow);

        // Wrap dim rect and panel into a single container for unified fade-out.
        var container = new Canvas { IsHitTestVisible = true };
        container.Children.Add(dimRect);

        panel.Loaded += (_, _) =>
        {
            Canvas.SetLeft(panel, (container.ActualWidth - panel.ActualWidth) / 2);
            Canvas.SetTop(panel, (container.ActualHeight - panel.ActualHeight) / 2);
        };
        container.Children.Add(panel);

        container.SizeChanged += (_, e) =>
        {
            dimRect.Width = e.NewSize.Width;
            dimRect.Height = e.NewSize.Height;
            Canvas.SetLeft(panel, (e.NewSize.Width - panel.ActualWidth) / 2);
            Canvas.SetTop(panel, (e.NewSize.Height - panel.ActualHeight) / 2);
        };

        Canvas.SetLeft(container, 0);
        Canvas.SetTop(container, 0);
        container.Width = ActualWidth > 0 ? ActualWidth : 1280;
        container.Height = ActualHeight > 0 ? ActualHeight : 720;

        container.Opacity = 0;
        OverlayCanvas.Children.Add(container);
        _banInstructionLayer = container;

        container.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        // Remove click-through so we can receive the scroll wheel event.
        SetClickThrough(false);
        MouseWheel += OnBanScreenScrollDetected;
    }

    /// <summary>
    /// Hides the ban instruction overlay. Safe to call even if it was already
    /// hidden (e.g. dismissed by the player's scroll wheel earlier).
    /// </summary>
    public void HideBanInstructionOverlay()
    {
        MouseWheel -= OnBanScreenScrollDetected;
        SetClickThrough(true);

        if (_banInstructionLayer == null)
            return;

        var layer = _banInstructionLayer;
        _banInstructionLayer = null;

        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        anim.Completed += (_, _) => OverlayCanvas.Children.Remove(layer);
        layer.BeginAnimation(OpacityProperty, anim);
    }

    private void OnBanScreenScrollDetected(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        HideBanInstructionOverlay();

        // Forward the scroll to the game window so the player's first scroll is not swallowed.
        var gameHwnd = User32.FindArknightsWindow();
        if (gameHwnd != IntPtr.Zero)
        {
            User32.GetCursorPos(out var cursorPos);
            var wParam = (IntPtr)((e.Delta << 16) & 0xFFFF0000);
            var lParam = (IntPtr)(((cursorPos.Y & 0xFFFF) << 16) | (cursorPos.X & 0xFFFF));
            User32.SendMessage(gameHwnd, User32.WM_MOUSEWHEEL, wParam, lParam);
        }
    }

    // ?? Ban avatar markers ????????????????????????????????????????????????????

    /// <summary>
    /// Places (or repositions) a "banned" marker badge on each matched operator's
    /// portrait. <paramref name="imgWidth"/> and <paramref name="imgHeight"/> are
    /// the pixel dimensions of the screenshot that produced <paramref name="positions"/>,
    /// used to normalise hit-box coordinates to overlay space.
    /// </summary>
    public void UpdateBanMarkers(
        IReadOnlyList<System.Drawing.Rectangle> positions,
        int imgWidth, int imgHeight)
    {
        if (imgWidth <= 0 || imgHeight <= 0)
            return;

        // Markers are accumulative: once placed they stay until ClearBanMarkers().
        // Never remove a marker mid-session ? TemplateMatch can miss a slot on individual
        // frames, and removing+re-adding causes visible flicker.
        foreach (var hitBox in positions)
        {
            double mx = (double)hitBox.X / imgWidth * ActualWidth;
            double my = (double)hitBox.Y / imgHeight * ActualHeight;

            var marker = CreateBanMarker();
            marker.Opacity = 0;
            Canvas.SetLeft(marker, mx);
            Canvas.SetTop(marker, my);
            OverlayCanvas.Children.Add(marker);
            _banMarkerPool.Add(marker);

            marker.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        }
    }

    /// <summary>
    /// Removes all ban avatar markers with a fade-out animation.
    /// Called when the player leaves the ban screen.
    /// </summary>
    public void ClearBanMarkers()
    {
        foreach (var el in _banMarkerPool.ToList())
        {
            _banMarkerPool.Remove(el);
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            anim.Completed += (_, _) => OverlayCanvas.Children.Remove(el);
            el.BeginAnimation(OpacityProperty, anim);
        }
    }

    private static Grid CreateBanMarker()
    {
        var grid = new Grid
        {
            Width = BanMarkerSize,
            Height = BanMarkerSize
        };

        var ellipse = new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                ShadowDepth = 2,
                Opacity = 0.5
            }
        };

        var label = new TextBlock
        {
            Text = "\u7981",   // ?
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(ellipse);
        grid.Children.Add(label);
        return grid;
    }

    // ?? Helpers ???????????????????????????????????????????????????????????????

    /// <summary>
    /// Toggles the <c>WS_EX_TRANSPARENT</c> extended style on the overlay HWND.
    /// When <paramref name="clickThrough"/> is <c>true</c> (default state) the
    /// overlay is invisible to hit-testing and all input falls through to the game.
    /// When <c>false</c> the overlay can receive mouse events (used briefly while
    /// the instruction layer waits for the player's scroll).
    /// </summary>
    private void SetClickThrough(bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        if (clickThrough)
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
                exStyle | User32.WS_EX_TRANSPARENT);
        else
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
                exStyle & ~User32.WS_EX_TRANSPARENT);
    }
}
