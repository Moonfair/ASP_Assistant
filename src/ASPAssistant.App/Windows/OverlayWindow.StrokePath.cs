using System.Windows;
using ASPAssistant.App.Services;

namespace ASPAssistant.App.Windows;

internal sealed class MatchStrokePath
{
    private readonly List<MatchPoint> _points = [];
    internal IReadOnlyList<MatchPoint> Points => _points;

    internal void Add(Point point, Size viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0 ||
            !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        var sample = new MatchPoint(Math.Clamp(point.X / viewport.Width, 0, 1),
            Math.Clamp(point.Y / viewport.Height, 0, 1));
        if (_points.Count > 0 && _points[^1] == sample) return;
        _points.Add(sample);
        if (_points.Count <= 512) return;

        // Keep both endpoints and simplify the least visible bend, preserving long turns and reversals.
        int least = 1;
        double smallest = double.MaxValue;
        for (int i = 1; i < _points.Count - 1; i++)
        {
            var a = _points[i - 1];
            var b = _points[i];
            var c = _points[i + 1];
            double dx = c.X - a.X, dy = c.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared == 0 ? 0 :
                Math.Clamp(((b.X - a.X) * dx + (b.Y - a.Y) * dy) / lengthSquared, 0, 1);
            double ex = b.X - (a.X + t * dx), ey = b.Y - (a.Y + t * dy);
            double distance = ex * ex + ey * ey;
            if (distance >= smallest) continue;
            least = i;
            smallest = distance;
            if (distance == 0) break;
        }
        _points.RemoveAt(least);
    }

    internal MatchPoint[] Finish() => _points.Count == 1 ? [_points[0], _points[0]] : [.. _points];
}
