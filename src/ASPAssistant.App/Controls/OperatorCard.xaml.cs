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

    public static readonly RoutedEvent BanToggledEvent =
        EventManager.RegisterRoutedEvent(
            "BanToggled", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(OperatorCard));

    public event RoutedEventHandler BanToggled
    {
        add => AddHandler(BanToggledEvent, value);
        remove => RemoveHandler(BanToggledEvent, value);
    }

    public Func<string, bool>? IsTrackedCheck { get; set; }
    public bool IsTracked { get; set; }

    public Func<string, bool>? IsBannedCheck { get; set; }
    public bool IsBanned { get; set; }

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
        if (IsTrackedCheck != null && DataContext is Operator opT)
            IsTracked = IsTrackedCheck(opT.Name);

        // Inherit IsBannedCheck from parent view if not explicitly set
        if (IsBannedCheck == null)
            IsBannedCheck = FindAncestor<OperatorBrowseView>()?.IsBannedCheck;
        if (IsBannedCheck != null && DataContext is Operator opB)
            IsBanned = IsBannedCheck(opB.Name);

        if (DataContext is Operator op2)
        {
            LoadIcon(op2.IconPath);
        }
        UpdateVariantDisplay();
        UpdateTrackButton();
        UpdateBanButton();
        UpdateBannedDisplay();
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

    public void RefreshBannedState(bool isBanned)
    {
        IsBanned = isBanned;
        UpdateBanButton();
        UpdateBannedDisplay();
    }

    private void OnBanClick(object sender, RoutedEventArgs e)
    {
        IsBanned = !IsBanned;
        UpdateBanButton();
        UpdateBannedDisplay();
        RaiseEvent(new RoutedEventArgs(BanToggledEvent, this));
    }

    private void UpdateTrackButton()
    {
        TrackButton.Content = IsTracked ? "★ 追踪中" : "☆ 追踪";
        TrackButton.Tag = IsTracked ? "tracked" : "";
    }

    private void UpdateBanButton()
    {
        BanButton.Content = IsBanned ? "✕ 禁用中" : "禁用";
        BanButton.Tag = IsBanned ? "banned" : "";
    }

    private void UpdateBannedDisplay()
    {
        var banVis = IsBanned ? Visibility.Visible : Visibility.Collapsed;
        BanDimOverlay.Visibility = banVis;
        BanBadge.Visibility = banVis;
        // Slightly reduce overall card opacity to visually deprioritise banned operators
        Opacity = IsBanned ? 0.65 : 1.0;
    }
}
