using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Controls;

public partial class EquipmentCard : UserControl
{
    public static readonly RoutedEvent TrackingToggledEvent =
        EventManager.RegisterRoutedEvent(
            "TrackingToggled", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(EquipmentCard));

    public event RoutedEventHandler TrackingToggled
    {
        add => AddHandler(TrackingToggledEvent, value);
        remove => RemoveHandler(TrackingToggledEvent, value);
    }

    public bool IsTracked { get; set; }
    private bool _showElite;

    public EquipmentCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _showElite = false;
        UpdateVariantDisplay();
        UpdateTrackButton();
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
        if (DataContext is not Equipment eq) return;
        var variant = _showElite ? eq.Elite : eq.Normal;
        EffectText.Text = variant.EffectDescription;
    }

    private void OnTrackClick(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(TrackingToggledEvent, this));
    }

    private void UpdateTrackButton()
    {
        TrackButton.Content = IsTracked ? "★ 追踪中" : "☆ 追踪";
    }
}
