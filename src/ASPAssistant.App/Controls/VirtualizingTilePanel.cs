using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ASPAssistant.App.Controls;

/// <summary>Fixed-size catalog tiles, realizing the viewport plus one row on each side.</summary>
public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    private const double TileWidth = 76;
    private const double TileHeight = 90;
    private int _columns = 1;
    private Size _extent;
    private Size _viewport;
    private double _offset;

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return new Size();
        var width = double.IsInfinity(availableSize.Width) ? Math.Max(ActualWidth, TileWidth) : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? Math.Max(ActualHeight, TileHeight * 4) : availableSize.Height;
        _columns = Math.Max(1, (int)(width / TileWidth));
        _viewport = new Size(width, height);
        _extent = new Size(width, Math.Ceiling((double)owner.Items.Count / _columns) * TileHeight);
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _extent.Height - height));
        ScrollOwner?.InvalidateScrollInfo();

        var first = Math.Max(0, (int)(_offset / TileHeight) - 1) * _columns;
        var last = Math.Min(owner.Items.Count - 1, ((int)Math.Ceiling((_offset + height) / TileHeight) + 1) * _columns - 1);
        // Accessing InternalChildren connects the panel to its owner's generator on first measure.
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        var position = generator.GeneratorPositionFromIndex(first);
        var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
        using (generator.StartAt(position, GeneratorDirection.Forward, true))
        {
            for (var index = first; index <= last; index++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var created);
                if (created)
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                    else InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(new Size(TileWidth, TileHeight));
            }
        }
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var childPosition = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(childPosition);
            if (index >= first && index <= last) continue;
            generator.Remove(childPosition, 1);
            RemoveInternalChildRange(i, 1);
        }
        return _viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            InternalChildren[i].Arrange(new Rect(index % _columns * TileWidth,
                index / _columns * TileHeight - _offset, TileWidth, TileHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                if (args.ItemUICount > 0) RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Move:
                if (args.ItemUICount > 0) RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                RemoveInternalChildRange(0, InternalChildren.Count);
                _offset = 0;
                break;
        }
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        var count = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
        if (index < 0 || index >= count) return;
        RevealRow(index / _columns);
    }

    private void RevealRow(int row)
    {
        var top = row * TileHeight;
        if (top < _offset) SetVerticalOffset(top);
        else if (top + TileHeight > _offset + ViewportHeight) SetVerticalOffset(top + TileHeight - ViewportHeight);
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        DependencyObject? child = visual;
        while (child is not null && VisualTreeHelper.GetParent(child) != this) child = VisualTreeHelper.GetParent(child);
        if (child is not UIElement element) return Rect.Empty;
        var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(InternalChildren.IndexOf(element), 0));
        if (index < 0) return Rect.Empty;
        RevealRow(index / _columns);
        var bounds = new Rect(index % _columns * TileWidth, index / _columns * TileHeight - _offset, TileWidth, TileHeight);
        bounds.Intersect(new Rect(_viewport));
        return bounds;
    }

    public void SetVerticalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        var clamped = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (Math.Abs(_offset - clamped) < 0.01) return;
        _offset = clamped;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer? ScrollOwner { get; set; }
    public void LineUp() => SetVerticalOffset(_offset - TileHeight);
    public void LineDown() => SetVerticalOffset(_offset + TileHeight);
    public void PageUp() => SetVerticalOffset(_offset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(_offset + ViewportHeight);
    public void MouseWheelUp() => SetVerticalOffset(_offset - TileHeight * 3);
    public void MouseWheelDown() => SetVerticalOffset(_offset + TileHeight * 3);
    public void SetHorizontalOffset(double offset) { }
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}
