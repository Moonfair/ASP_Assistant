using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Mac;

internal sealed class MarkerWindow : Window
{
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };

    public MarkerWindow()
    {
        Title = "ASP Assistant 标记";
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = _canvas;
        IsHitTestVisible = false;
        Opened += (_, _) => MacWindowInteraction.MakeClickThrough(this);
    }

    public void Update(MacWorkspace workspace)
    {
        var target = workspace.GameSource.Current;
        var viewport = target == null ? null : workspace.Capture?.GetViewport(target) ?? target.GetRenderRect();
        if (viewport is not { Width: > 0, Height: > 0 } rect ||
            !workspace.WindowTracker.IsGameFocused)
        {
            Hide();
            return;
        }
        var items = workspace.CurrentGameState.ShopItems.Where(item => item.IsTracked).ToArray();
        if (items.Length == 0) { Hide(); return; }

        Width = rect.Width;
        Height = rect.Height;
        _canvas.Children.Clear();
        foreach (var item in items)
        {
            var (x, y, width, height) = item.OcrRegion;
            var border = new Border
            {
                Width = Math.Max(1, width * rect.Width), Height = Math.Max(1, height * rect.Height),
                Background = new SolidColorBrush(Color.FromArgb(65, 72, 237, 176)),
                BorderBrush = new SolidColorBrush(Color.Parse("#48EDB0")),
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(5),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(border, x * rect.Width);
            Canvas.SetTop(border, y * rect.Height);
            _canvas.Children.Add(border);
        }
        if (!IsVisible) Show();
        MacWindowInteraction.PositionAtQuartzTopLeft(this, rect.Left, rect.Top);
    }
}
