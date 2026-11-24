using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.App.Windows;

public partial class OverlayWindow
{
    private readonly DispatcherTimer _inkTimer = new(DispatcherPriority.Input);
    private readonly Dictionary<string, MatchStroke> _localInk = [];
    private MatchSession? _match;
    private Func<bool>? _canUseMarking;
    private MatchInkLayer? _inkLayer;
    private MatchStrokePath? _draft;
    private long _nextInkExpiry = long.MaxValue;
    private string? _inkRoom;
    private bool _ctrlWasDown;

    internal bool IsMatchMarking { get; private set; }
    internal event Action? MatchMarkingChanged;

    internal void ConfigureMatch(MatchSession session, Func<bool> canUseMarking)
    {
        _match = session;
        _canUseMarking = canUseMarking;
        _inkLayer = new MatchInkLayer(this) { IsHitTestVisible = false, ClipToBounds = true };
        Panel.SetZIndex(_inkLayer, 1);
        OverlayCanvas.Children.Add(_inkLayer);
        SizeInkLayer();
        _inkLayer.MouseLeftButtonDown += BeginInk;
        _inkLayer.MouseMove += ContinueInk;
        _inkLayer.MouseLeftButtonUp += CommitInk;
        _inkLayer.LostMouseCapture += (_, _) => CancelInk();
        OverlayCanvas.SizeChanged += (_, _) => SizeInkLayer();
        _inkTimer.Interval = TimeSpan.FromMilliseconds(32);
        _inkTimer.Tick += (_, _) => RefreshMatchInput();
        session.Changed += OnMatchChanged;
        IsVisibleChanged += (_, _) => OnMatchChanged();
        Closed += (_, _) =>
        {
            session.Changed -= OnMatchChanged;
            _inkTimer.Stop();
            CancelInk();
        };
        OnMatchChanged();
    }

    private void SizeInkLayer()
    {
        if (_inkLayer is not { } layer) return;
        layer.Width = OverlayCanvas.ActualWidth;
        layer.Height = OverlayCanvas.ActualHeight;
        layer.InvalidateVisual();
    }

    private void OnMatchChanged()
    {
        var room = _match?.State is { Phase: "playing", GameRoomId: { } gameRoom } state &&
            gameRoom == _match.Observation?.RoomId ? state.RoomId : null;
        if (_inkRoom != room)
        {
            _inkRoom = room;
            ResetMatchInput();
            _localInk.Clear();
        }
        _inkTimer.IsEnabled = room != null && IsVisible;
        if (_localInk.Count != 0 && _match != null)
            PruneLocalInk(_match.ServerTimeNow);
        RefreshMatchInput();
        _inkLayer?.InvalidateVisual();
    }

    internal void ToggleMatchMarking()
    {
        RefreshMatchInput();
        if (MatchInputAvailable()) SetMatchMarking(!IsMatchMarking, finishDraft: true);
    }

    private bool MatchForeground() => _inkRoom != null && IsVisible &&
        _bounds is { Width: > 0, Height: > 0 } && _canUseMarking?.Invoke() == true;

    private bool MatchInputAvailable() =>
        MatchForeground() && _match?.IsRoomReadPending == false && !IsCalibratingMarkers && _banInstructionLayer == null;

    private void RefreshMatchInput()
    {
        if (_inkLayer is not { } layer || _match == null) return;
        bool foreground = MatchForeground();
        layer.Visibility = foreground ? Visibility.Visible : Visibility.Collapsed;
        bool eligible = foreground && !_match.IsRoomReadPending && !IsCalibratingMarkers && _banInstructionLayer == null;
        if (eligible)
        {
            bool ctrlDown = User32.IsControlPressed();
            if (ctrlDown != _ctrlWasDown) SetMatchMarking(ctrlDown, finishDraft: true);
            _ctrlWasDown = ctrlDown;
        }
        else ResetMatchInput();

        long now = _match.ServerTimeNow;
        if (now >= _nextInkExpiry)
        {
            PruneLocalInk(now);
            _nextInkExpiry = long.MaxValue;
            layer.InvalidateVisual();
        }
    }

    private void PruneLocalInk(long now)
    {
        foreach (var pair in _localInk.Where(pair => pair.Value.ExpiresAt <= now).ToArray())
            _localInk.Remove(pair.Key);
    }

    private void ResetMatchInput()
    {
        _ctrlWasDown = false;
        SetMatchMarking(false);
    }

    private void SetMatchMarking(bool enabled, bool finishDraft = false)
    {
        if (enabled == IsMatchMarking || _inkLayer is not { } layer) return;
        IsMatchMarking = enabled;
        if (!enabled)
        {
            if (finishDraft) CommitDraft();
            else CancelInk();
        }
        layer.IsHitTestVisible = enabled;
        layer.Cursor = enabled ? Cursors.Cross : null;
        layer.InvalidateVisual();
        RefreshMarkerInput();
        MatchMarkingChanged?.Invoke();
    }

    private void BeginInk(object sender, MouseButtonEventArgs e)
    {
        RefreshMatchInput();
        if (!IsMatchMarking || _inkLayer is not { } layer || !layer.CaptureMouse()) return;
        _draft = new MatchStrokePath();
        AddInkSample(e);
        e.Handled = true;
    }

    private void ContinueInk(object sender, MouseEventArgs e)
    {
        if (_draft == null) return;
        RefreshMatchInput();
        if (e.LeftButton == MouseButtonState.Pressed) AddInkSample(e);
        else CancelInk();
        e.Handled = true;
    }

    private void AddInkSample(MouseEventArgs e)
    {
        if (_draft == null || _inkLayer is not { } layer) return;
        _draft.Add(e.GetPosition(layer), layer.RenderSize);
        layer.InvalidateVisual();
    }

    private void CommitInk(object sender, MouseButtonEventArgs e)
    {
        RefreshMatchInput();
        if (_draft == null) return;
        AddInkSample(e);
        CommitDraft();
        e.Handled = true;
    }

    private void CommitDraft()
    {
        if (_draft is not { } path || _match?.State is not { } state) return;
        var points = path.Finish();
        CancelInk();
        if (points.Length == 0) return;
        var id = Guid.NewGuid().ToString("D");
        _localInk.Add(id, new MatchStroke(id, state.SelfId, points, _match.ServerTimeNow + 10_000));
        _inkLayer?.InvalidateVisual();
        if (_match.CanSendStroke) _ = _match.StrokeAsync(id, points);
    }

    private void CancelInk()
    {
        _draft = null;
        if (_inkLayer is not { } layer) return;
        if (layer.IsMouseCaptured) layer.ReleaseMouseCapture();
        layer.InvalidateVisual();
    }

    private sealed class MatchInkLayer(OverlayWindow owner) : FrameworkElement
    {
        private readonly SolidColorBrush _dim = new(Color.FromArgb(0x33, 128, 128, 128));
        private readonly Dictionary<string, Pen> _pens = [];

        protected override void OnRender(DrawingContext dc)
        {
            if (owner._match?.State is not { Phase: "playing" } state) return;
            if (owner.IsMatchMarking) dc.DrawRectangle(_dim, null, new Rect(RenderSize));
            long now = owner._match.ServerTimeNow;
            owner._nextInkExpiry = long.MaxValue;
            foreach (var stroke in state.Strokes)
            {
                // Locally completed strokes have their own ten-second lifetime, independent of network latency.
                if (stroke.PlayerId != state.SelfId && !state.Muted.Contains(stroke.PlayerId))
                    DrawStroke(stroke);
            }
            foreach (var stroke in owner._localInk.Values) DrawStroke(stroke);
            if (owner._draft is { } path) Draw(path.Points, state.SelfId);
            if (owner._match.IsSynchronized && owner._inkRoom == state.RoomId &&
                OverlayShout.Latest(state, now) is { } shout && ActualWidth >= 100)
            {
                OverlayShout.Draw(dc, RenderSize, VisualTreeHelper.GetDpi(this).PixelsPerDip, state, shout);
                owner._nextInkExpiry = Math.Min(owner._nextInkExpiry, shout.SentAt + OverlayShout.DurationMs);
            }

            void DrawStroke(MatchStroke stroke)
            {
                if (stroke.ExpiresAt <= now) return;
                owner._nextInkExpiry = Math.Min(owner._nextInkExpiry, stroke.ExpiresAt);
                Draw(stroke.Points, stroke.PlayerId);
            }

            void Draw(IReadOnlyList<MatchPoint> points, string player)
            {
                if (points.Count == 0 || state.Members.FirstOrDefault(m => m.Id == player) is not { } member) return;
                if (member.Color is not { Length: 7 } color || color[0] != '#') return;
                if (!_pens.TryGetValue(color, out var pen))
                {
                    if (!int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return;
                    pen = new Pen(new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)), 4)
                    {
                        StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
                    };
                    pen.Freeze();
                    _pens[color] = pen;
                }
                Point Position(MatchPoint p) => new(p.X * ActualWidth, p.Y * ActualHeight);
                var first = Position(points[0]);
                if (points.Count == 1 || (points.Count == 2 && points[0] == points[1]))
                {
                    dc.DrawEllipse(pen.Brush, null, first, pen.Thickness / 2, pen.Thickness / 2);
                    return;
                }
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(first, false, false);
                    for (int i = 1; i < points.Count; i++) ctx.LineTo(Position(points[i]), true, true);
                }
                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }
        }
    }
}
