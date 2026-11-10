using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.App.Controls;

/// <summary>A visual-only drag follower; native drag/drop still uses the actual pointer.</summary>
internal sealed class LineupDragPreview : Adorner, IDisposable
{
    private readonly AdornerDecorator _host;
    private readonly Border _card;
    private readonly Image _image;
    private readonly TranslateTransform _translation = new();
    private readonly Func<Point?> _readPointer;
    private TimeSpan? _lastFrame;
    private bool _hasPosition;
    private bool _disposed;

    internal static LineupDragPreview? Start(FrameworkElement source, string iconPath, string name,
        Func<Point?>? readPointer = null)
    {
        if (Window.GetWindow(source)?.Content is not AdornerDecorator { Child: not null } host) return null;
        return new LineupDragPreview(host, iconPath, name, readPointer);
    }

    private LineupDragPreview(AdornerDecorator host, string iconPath, string name, Func<Point?>? readPointer)
        : base(host.Child)
    {
        _host = host;
        _readPointer = readPointer ?? ReadPointer;
        IsHitTestVisible = false;
        IsEnabled = false;
        ClipToBounds = true;
        _image = new Image { Width = 58, Height = 58, Stretch = Stretch.Uniform };
        var placeholderStyle = new Style(typeof(TextBlock));
        placeholderStyle.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        var missingImage = new DataTrigger { Binding = new Binding(nameof(Image.Source)) { Source = _image }, Value = null };
        missingImage.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        placeholderStyle.Triggers.Add(missingImage);
        var content = new Grid();
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(name) ? "?" : name[..Math.Min(2, name.Length)],
            Style = placeholderStyle,
            Foreground = Brushes.White, FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(_image);
        _card = new Border
        {
            Width = 66, Height = 66, CornerRadius = new CornerRadius(7), Padding = new Thickness(3),
            Background = new SolidColorBrush(Color.FromRgb(37, 37, 63)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 182, 248)), BorderThickness = new Thickness(1),
            RenderTransform = _translation, Child = content
        };
        AddVisualChild(_card);
        DeferredIcon.SetPath(_image, iconPath);
        _host.AdornerLayer.Add(this);
        Advance(TimeSpan.Zero);
        CompositionTarget.Rendering += OnRendering;
        _host.Unloaded += OnUnloaded;
    }

    // WPF mouse positions can be stale inside the native OLE drag loop. Read the screen
    // pointer each rendered frame and let WPF convert physical pixels to local DIPs.
    private Point? ReadPointer() => PresentationSource.FromVisual(AdornedElement) is not null
        && User32.GetCursorPos(out var point)
        ? AdornedElement.PointFromScreen(new Point(point.X, point.Y)) : null;

    private void OnRendering(object? sender, EventArgs args)
    {
        var now = ((RenderingEventArgs)args).RenderingTime;
        var elapsed = _lastFrame is { } previous ? now - previous : TimeSpan.Zero;
        _lastFrame = now;
        Advance(elapsed);
    }

    internal void Advance(TimeSpan elapsed)
    {
        if (_disposed) return;
        var pointer = _readPointer();
        if (pointer is not { } cursor || !new Rect(AdornedElement.RenderSize).Contains(cursor))
        {
            _card.Opacity = 0;
            _hasPosition = false;
            return;
        }
        var target = cursor + new Vector(14, 16);
        var position = _hasPosition ? Follow(new Point(_translation.X, _translation.Y), target, elapsed) : target;
        _translation.X = position.X;
        _translation.Y = position.Y;
        _card.Opacity = 0.88;
        _hasPosition = true;
    }

    internal static Point Follow(Point current, Point target, TimeSpan elapsed)
    {
        // A 50 ms time constant gives a small trailing effect at any refresh rate.
        var amount = 1 - Math.Exp(-Math.Max(0, elapsed.TotalSeconds) / 0.05);
        return current + (target - current) * amount;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? _card : throw new ArgumentOutOfRangeException(nameof(index));
    protected override Size MeasureOverride(Size constraint)
    {
        _card.Measure(new Size(66, 66));
        return AdornedElement.RenderSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        _card.Arrange(new Rect(0, 0, 66, 66));
        return finalSize;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
        _host.Unloaded -= OnUnloaded;
        _host.AdornerLayer.Remove(this);
        DeferredIcon.SetPath(_image, "");
    }
}
