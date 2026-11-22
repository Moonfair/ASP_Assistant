using System.Windows;
using System.Windows.Input;

namespace ASPAssistant.App.Windows;

public partial class MemoryReadingConsentWindow : Window
{
    internal bool AllowMemoryReading { get; private set; }

    public MemoryReadingConsentWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RestrictedButton.Focus();
    }

    private void OnRestricted(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnFull(object sender, RoutedEventArgs e)
    {
        AllowMemoryReading = true;
        DialogResult = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
