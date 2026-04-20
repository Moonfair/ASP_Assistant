using System.Windows;
using System.Windows.Input;

namespace ASPAssistant.App.Windows;

/// <summary>
/// Generic single-line input dialog. Returns user input via <see cref="InputText"/> when OK
/// is clicked (DialogResult = true).
/// </summary>
public partial class InputDialog : Window
{
    public string InputText => InputBox.Text;

    public InputDialog(string title, string prompt, string initialText = "")
    {
        InitializeComponent();
        TitleText.Text = title;
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialText;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
