using System.Windows;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    internal void ConfigureMatch(MatchSession session, SettingsManager settings, IReadOnlyList<Strategy> strategies,
        CovenantCatalog covenants, OverlayWindow overlay)
    {
        MatchView.Configure(session, settings, strategies, covenants, overlay);
        var closed = false;
        void Refresh()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(Refresh);
                return;
            }
            if (closed) return;
            MatchView.Refresh();
            var confirming = MatchView.ShowConfirmationTakeover;
            if (confirming == (MatchTakeover.Visibility == Visibility.Visible)) return;

            if (confirming)
            {
                MatchTabHost.Content = null;
                StandardSurface.Visibility = Visibility.Collapsed;
                MatchTakeover.Content = MatchView;
                MatchTakeover.Visibility = Visibility.Visible;
                BringForward();
                MatchView.FocusConfirmation();
            }
            else
            {
                MatchTakeover.Content = null;
                MatchTakeover.Visibility = Visibility.Collapsed;
                MatchTabHost.Content = MatchView;
                MainTabs.SelectedItem = MatchTab;
                if (SettingsPage.Visibility == Visibility.Visible) SetSettingsVisible(false);
                StandardSurface.Visibility = Visibility.Visible;
            }
        }

        session.Changed += Refresh;
        overlay.MatchMarkingChanged += Refresh;
        MatchView.ConfirmationExpired += Refresh;
        Closed += (_, _) =>
        {
            closed = true;
            session.Changed -= Refresh;
            overlay.MatchMarkingChanged -= Refresh;
            MatchView.ConfirmationExpired -= Refresh;
            MatchView.Finish();
        };
        Refresh();
    }
}
