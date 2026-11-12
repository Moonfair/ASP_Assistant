using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ASPAssistant.App.Controls;

internal sealed class LineupDiagramPreview : ScrollViewer
{
    private readonly Canvas _canvas;
    private readonly ScaleTransform _zoom = new();
    private MouseButton? _panButton;
    private Point _panStart;
    private Vector _panOffsets;

    internal LineupDiagramPreview(Canvas canvas)
    {
        _canvas = canvas;
        canvas.LayoutTransform = _zoom;
        canvas.HorizontalAlignment = HorizontalAlignment.Left;
        canvas.VerticalAlignment = VerticalAlignment.Top;
        canvas.Cursor = Cursors.Hand;
        Content = canvas;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        MaxHeight = 430;
        ToolTip = "按住鼠标左键或中键拖动，滚轮缩放";
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;
        var oldScale = _zoom.ScaleX;
        var newScale = Math.Clamp(oldScale * Math.Pow(1.2, e.Delta / 120.0), 0.15, 2.5);
        if (newScale == oldScale) return;

        var anchor = e.GetPosition(_canvas);
        _zoom.ScaleX = _zoom.ScaleY = newScale;
        ScrollToHorizontalOffset(HorizontalOffset + anchor.X * (newScale - oldScale));
        ScrollToVerticalOffset(VerticalOffset + anchor.Y * (newScale - oldScale));
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle) || Mouse.Captured is not null ||
            (ScrollableWidth == 0 && ScrollableHeight == 0) ||
            e.OriginalSource is not Visual source || (source != _canvas && !_canvas.IsAncestorOf(source)) ||
            !CaptureMouse()) return;

        _panButton = e.ChangedButton;
        _panStart = e.GetPosition(this);
        _panOffsets = new Vector(HorizontalOffset, VerticalOffset);
        Cursor = Cursors.SizeAll;
        ForceCursor = true;
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_panButton is null) return;

        var delta = e.GetPosition(this) - _panStart;
        ScrollToHorizontalOffset(_panOffsets.X - delta.X);
        ScrollToVerticalOffset(_panOffsets.Y - delta.Y);
        e.Handled = true;
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        if (e.ChangedButton != _panButton) return;

        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_panButton is null || IsMouseCaptured) return;

        _panButton = null;
        ClearValue(CursorProperty);
        ClearValue(ForceCursorProperty);
    }
}
