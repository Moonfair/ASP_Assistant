using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    private int _cannotSpeechVersion;

    internal void ShowCannotSpeech(string caption, TimeSpan duration)
    {
        if (!IsVisible || WindowState == WindowState.Minimized) return;

        int version = ++_cannotSpeechVersion;
        var hold = TimeSpan.FromSeconds(Math.Max(2.5, duration.TotalSeconds));
        var fade = TimeSpan.FromMilliseconds(300);
        CannotSpeechText.Text = caption;
        CannotSpeech.Visibility = Visibility.Visible;
        PositionCannotSpeech();

        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(hold)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(hold + fade)));
        opacity.Completed += (_, _) =>
        {
            if (_cannotSpeechVersion == version) HideCannotSpeech();
        };

        var motion = new DoubleAnimation(6, -6, hold + fade)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        CannotSpeechMotion.BeginAnimation(TranslateTransform.YProperty, motion);
        CannotSpeech.BeginAnimation(OpacityProperty, opacity);
    }

    private void PositionCannotSpeech()
    {
        if (!CannotSpeech.IsVisible) return;
        var anchor = SettingsPage.IsVisible ? CannotPreviewPortrait : CannotPortrait;
        if (!anchor.IsVisible) return;
        var right = anchor.TranslatePoint(new Point(anchor.ActualWidth, 0), CannotSpeechLayer);
        double left = right.X + 5;
        double maxWidth = CannotSpeechLayer.ActualWidth - left - 8;
        if (maxWidth < 95)
        {
            left = 8;
            maxWidth = Math.Max(95, CannotSpeechLayer.ActualWidth - 16);
        }
        Canvas.SetLeft(CannotSpeech, left);
        Canvas.SetTop(CannotSpeech, Math.Max(7, right.Y));
        CannotSpeech.MaxWidth = Math.Min(268, maxWidth);
    }

    internal void HideCannotSpeech()
    {
        _cannotSpeechVersion++;
        CannotSpeech.BeginAnimation(OpacityProperty, null);
        CannotSpeechMotion.BeginAnimation(TranslateTransform.YProperty, null);
        CannotSpeech.Visibility = Visibility.Collapsed;
    }
}
