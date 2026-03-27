using System.Windows;
using System.Windows.Controls;
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
        if (DataContext is Operator op && IsTrackedCheck != null)
            IsTracked = IsTrackedCheck(op.Name);
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
        if (DataContext is not Operator op) return;
        var variant = _showElite ? op.Elite : op.Normal;
        TraitTypeText.Text = variant.TraitType;
        TraitDescriptionText.Text = variant.TraitDescription;
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
