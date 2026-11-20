using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    internal void ConfigureBobChatter(int intervalSeconds, Action<int> change)
    {
        BobChatterIntervalSlider.Value = Math.Clamp(intervalSeconds,
            BobVoiceTriggers.MinimumChatterIntervalSeconds, BobVoiceTriggers.MaximumChatterIntervalSeconds);
        UpdateChatterIntervalLabel();
        BobChatterIntervalSlider.ValueChanged += (_, _) =>
        {
            UpdateChatterIntervalLabel();
            change((int)Math.Round(BobChatterIntervalSlider.Value));
        };
    }

    private void UpdateChatterIntervalLabel()
    {
        int interval = (int)Math.Round(BobChatterIntervalSlider.Value);
        BobChatterIntervalLabel.Text = $"{interval}～{interval * 2} 秒";
    }

    internal void ConfigureSidePanelOpacity(double opacity, Action<double> change)
    {
        Opacity = double.IsFinite(opacity)
            ? Math.Clamp(opacity, SettingsManager.MinimumSidePanelOpacity, 1) : 1;
        SidePanelOpacitySlider.Value = Opacity * 100;
        SidePanelOpacityLabel.Text = $"{SidePanelOpacitySlider.Value:0}%";
        SidePanelOpacitySlider.ValueChanged += (_, _) =>
        {
            Opacity = SidePanelOpacitySlider.Value / 100;
            SidePanelOpacityLabel.Text = $"{SidePanelOpacitySlider.Value:0}%";
            change(Opacity);
        };
    }
}
