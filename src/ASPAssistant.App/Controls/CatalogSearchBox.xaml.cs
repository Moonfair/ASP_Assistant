using System.Windows;
using System.Windows.Controls;

namespace ASPAssistant.App.Controls;

public partial class CatalogSearchBox : UserControl
{
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(CatalogSearchBox), new PropertyMetadata("搜索名称"));

    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public CatalogSearchBox() => InitializeComponent();

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Input.Clear();
        Input.Focus();
    }
}
