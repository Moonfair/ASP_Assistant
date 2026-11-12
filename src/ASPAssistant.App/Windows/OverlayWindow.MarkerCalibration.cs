using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class OverlayWindow
{
    private readonly SettingsManager _settings;
    private MarkerOffset _markerOffset;
    private MarkerDrag? _drag;

    internal bool IsCalibratingMarkers { get; private set; }
    internal event Action<bool>? MarkerOffsetSaveCompleted;

    internal void SetMarkerCalibration(bool enabled)
    {
        if (!enabled) EndMarkerDrag(save: false);
        IsCalibratingMarkers = enabled;
        RefreshMarkerInput();
    }

    internal void ResetMarkerOffset()
    {
        EndMarkerDrag(save: false);
        if (SaveMarkerOffset(default))
        {
            _markerOffset = default;
            RepositionMarkers();
        }
    }

    private void InitializeMarkerCalibration()
    {
        OverlayCanvas.MouseMove += (_, e) =>
        {
            if (_drag == null) return;
            if (e.LeftButton == MouseButtonState.Pressed)
                MoveMarkerDrag(e.GetPosition(OverlayCanvas));
            else
                EndMarkerDrag(save: true);
        };
        OverlayCanvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_drag == null) return;
            MoveMarkerDrag(e.GetPosition(OverlayCanvas));
            EndMarkerDrag(save: true);
            e.Handled = true;
        };
        OverlayCanvas.LostMouseCapture += (_, _) => EndMarkerDrag(save: false);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) EndMarkerDrag(save: false);
        };
        OverlayCanvas.SizeChanged += (_, _) =>
        {
            EndMarkerDrag(save: false);
            RepositionMarkers();
        };
    }

    private void RefreshMarkerInput()
    {
        SetClickThrough(!IsCalibratingMarkers && _banInstructionLayer == null);
        OverlayCanvas.Cursor = IsCalibratingMarkers && _banInstructionLayer == null ? Cursors.SizeAll : null;
    }

    private void AddMarker(OverlayMarker marker)
    {
        var element = marker.Element;
        element.MouseLeftButtonDown += (_, e) => BeginMarkerDrag(marker, e);
        element.Opacity = 0;
        PositionMarker(marker);
        OverlayCanvas.Children.Add(element);
        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    private void BeginMarkerDrag(OverlayMarker marker, MouseButtonEventArgs e)
    {
        if (!IsCalibratingMarkers || _banInstructionLayer != null || _drag != null ||
            OverlayCanvas.ActualWidth <= 0 || OverlayCanvas.ActualHeight <= 0 || !OverlayCanvas.CaptureMouse())
            return;

        _drag = new MarkerDrag(marker, e.GetPosition(OverlayCanvas),
            new Point(Canvas.GetLeft(marker.Element), Canvas.GetTop(marker.Element)), _markerOffset);
        e.Handled = true;
    }

    private void MoveMarkerDrag(Point pointer)
    {
        if (_drag is not { } drag) return;

        var delta = pointer - drag.Pointer;
        var origin = GetMarkerOrigin(drag.Marker);
        var position = ClampMarkerPosition(drag.Marker, drag.Position + delta);
        _markerOffset = new MarkerOffset(
            delta.X == 0 ? drag.Offset.X : (position.X - origin.X) / OverlayCanvas.ActualWidth,
            delta.Y == 0 ? drag.Offset.Y : (position.Y - origin.Y) / OverlayCanvas.ActualHeight);
        RepositionMarkers();
    }

    private void EndMarkerDrag(bool save)
    {
        if (_drag is not { } drag) return;
        _drag = null;
        if (OverlayCanvas.IsMouseCaptured) OverlayCanvas.ReleaseMouseCapture();

        if (_markerOffset == drag.Offset) return;
        if (save && SaveMarkerOffset(_markerOffset)) return;

        _markerOffset = drag.Offset;
        RepositionMarkers();
    }

    private bool SaveMarkerOffset(MarkerOffset offset)
    {
        try { _settings.SaveMarkerOffset(offset); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Settings", "Could not save marker offset", ex);
            MarkerOffsetSaveCompleted?.Invoke(false);
            return false;
        }

        MarkerOffsetSaveCompleted?.Invoke(true);
        return true;
    }

    private void RepositionMarkers()
    {
        foreach (var marker in _markerPool.Values) PositionMarker(marker);
        foreach (var marker in _banMarkerPool) PositionMarker(marker);
    }

    private void PositionMarker(OverlayMarker marker)
    {
        var offset = new Vector(_markerOffset.X * OverlayCanvas.ActualWidth, _markerOffset.Y * OverlayCanvas.ActualHeight);
        var position = ClampMarkerPosition(marker, GetMarkerOrigin(marker) + offset);
        Canvas.SetLeft(marker.Element, position.X);
        Canvas.SetTop(marker.Element, position.Y);
    }

    private Point GetMarkerOrigin(OverlayMarker marker) => ClampMarkerPosition(marker,
        new Point(marker.Anchor.X * OverlayCanvas.ActualWidth, marker.Anchor.Y * OverlayCanvas.ActualHeight) + marker.Inset);

    private Point ClampMarkerPosition(OverlayMarker marker, Point position) => new(
        Math.Clamp(position.X, 0, Math.Max(0, OverlayCanvas.ActualWidth - marker.Element.Width)),
        Math.Clamp(position.Y, 0, Math.Max(0, OverlayCanvas.ActualHeight - marker.Element.Height)));

    private sealed class OverlayMarker(Grid element, Point anchor, Vector inset)
    {
        public Grid Element { get; } = element;
        public Point Anchor { get; set; } = anchor;
        public Vector Inset { get; } = inset;
    }

    private sealed record MarkerDrag(OverlayMarker Marker, Point Pointer, Point Position, MarkerOffset Offset);
}
