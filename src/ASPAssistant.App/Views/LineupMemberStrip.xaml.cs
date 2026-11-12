using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class LineupMemberStrip : UserControl
{
    private FrameworkElement? _dragSource;
    private Point _pressPoint;
    private (string Format, string Name, string Path) _dragIcon;

    public bool CanDragIcons { get; set; }

    public LineupMemberStrip() => InitializeComponent();

    private void OnIconMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanDragIcons || sender is not FrameworkElement source)
            return;
        var (format, name, path) = source.DataContext switch
        {
            LineupMemberViewModel member => (LineupDragDrop.OperatorFormat, member.OperatorName, member.IconPath),
            LineupEquipmentSlotViewModel slot => (LineupDragDrop.EquipmentFormat, slot.Name, slot.IconPath),
            _ => ("", "", "")
        };
        if (string.IsNullOrWhiteSpace(name) || !source.CaptureMouse()) return;
        _dragSource = source;
        _pressPoint = e.GetPosition(this);
        _dragIcon = (format, name, path);
    }

    private void OnIconMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource is not { } source) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndIconPress();
            return;
        }
        var delta = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var (format, name, path) = _dragIcon;
        EndIconPress();
        e.Handled = true;
        using var preview = LineupDragPreview.Start(this, path, name);
        DragDrop.DoDragDrop(source, new DataObject(format, name), DragDropEffects.Copy);
    }

    private void OnIconMouseUp(object sender, MouseButtonEventArgs e) => EndIconPress();

    private void OnIconLostCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, _dragSource)) _dragSource = null;
    }

    private void EndIconPress()
    {
        var source = _dragSource;
        _dragSource = null;
        if (source?.IsMouseCaptured == true) source.ReleaseMouseCapture();
    }
}
