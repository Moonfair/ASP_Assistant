using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Controls;

public partial class OperatorCard : UserControl
{
    public static readonly RoutedEvent TrackingToggledEvent =
        EventManager.RegisterRoutedEvent(
            "TrackingToggled", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(OperatorCard));

    public event RoutedEventHandler TrackingToggled
    {
        add => AddHandler(TrackingToggledEvent, value);
        remove => RemoveHandler(TrackingToggledEvent, value);
    }

    public Func<string, bool>? IsTrackedCheck { get; set; }
    public bool IsTracked { get; set; }
    private bool _showElite;

    public OperatorCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _showElite = false;
        // Inherit IsTrackedCheck from parent view if not explicitly set
        if (IsTrackedCheck == null)
            IsTrackedCheck = FindAncestor<OperatorBrowseView>()?.IsTrackedCheck;
        if (DataContext is Operator op && IsTrackedCheck != null)
            IsTracked = IsTrackedCheck(op.Name);
        if (DataContext is Operator op2)
        {
            LoadIcon(op2.IconPath);
        }
        UpdateVariantDisplay();
        UpdateTrackButton();
    }

    private T? FindAncestor<T>() where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnNormalClick(object sender, RoutedEventArgs e)
    {
        _showElite = false;
        NormalToggle.IsChecked = true;
        EliteToggle.IsChecked = false;
        UpdateVariantDisplay();
    }

    private void OnEliteClick(object sender, RoutedEventArgs e)
    {
        _showElite = true;
        NormalToggle.IsChecked = false;
        EliteToggle.IsChecked = true;
        UpdateVariantDisplay();
    }

    private void UpdateVariantDisplay()
    {
        if (DataContext is not Operator op) return;
        var variant = _showElite ? op.Elite : op.Normal;
        TraitsPanel.ItemsSource = variant.Traits;
    }

    private void OnTrackClick(object sender, RoutedEventArgs e)
    {
        IsTracked = !IsTracked;
        // #region agent log
        var opName = (DataContext as ASPAssistant.Core.Models.Operator)?.Name ?? "?";
        System.IO.File.AppendAllText(@"c:\PlayGround\ASPA\ASP_Assistant\debug-0aa719.log", System.Text.Json.JsonSerializer.Serialize(new { sessionId = "0aa719", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), location = "OperatorCard.xaml.cs:OnTrackClick", message = "Track button clicked, local IsTracked toggled", data = new { opName, newIsTracked = IsTracked }, hypothesisId = "A" }) + "\n");
        // #endregion
        UpdateTrackButton();
        RaiseEvent(new RoutedEventArgs(TrackingToggledEvent, this));
    }

    private void LoadIcon(string iconPath)
    {
        if (string.IsNullOrEmpty(iconPath))
            return;
        var fullPath = Path.Combine(AppContext.BaseDirectory, "data", iconPath);
        if (File.Exists(fullPath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            IconImage.Source = bitmap;
        }
    }

    public void RefreshTrackedState(bool isTracked)
    {
        IsTracked = isTracked;
        UpdateTrackButton();
    }

    private void UpdateTrackButton()
    {
        TrackButton.Content = IsTracked ? "★ 追踪中" : "☆ 追踪";
        TrackButton.Tag = IsTracked ? "tracked" : "";
    }
}
