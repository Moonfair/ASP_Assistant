using System.Windows;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    public event Action? SelectGameSourceRequested;

    public void SetGameSourceStatus(string status)
    {
        if (GameSourceStatus.Text != status) GameSourceStatus.Text = status;
    }

    private void OnSelectGameSource(object sender, RoutedEventArgs e) => SelectGameSourceRequested?.Invoke();
}
