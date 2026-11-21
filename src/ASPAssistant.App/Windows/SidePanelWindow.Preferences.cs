using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    internal void ConfigureBobChatter(double frequency, Action<double> change)
    {
        BobChatterFrequencySlider.Value = double.IsFinite(frequency)
            ? Math.Clamp(frequency, 0, 1) : BobVoiceTriggers.DefaultChatterFrequency;
        UpdateChatterFrequencyLabel();
        BobChatterFrequencySlider.ValueChanged += (_, _) =>
        {
            UpdateChatterFrequencyLabel();
            change(BobChatterFrequencySlider.Value);
        };
    }

    private void UpdateChatterFrequencyLabel() =>
        BobChatterFrequencyLabel.Text = $"{BobChatterFrequencySlider.Value:0.00}";

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
