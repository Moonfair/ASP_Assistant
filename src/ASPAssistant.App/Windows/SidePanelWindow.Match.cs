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
        }

        BattleData.Updated += session.Observe;
        session.Changed += Refresh;
        overlay.MatchMarkingChanged += Refresh;
        Closed += (_, _) =>
        {
            closed = true;
            BattleData.Updated -= session.Observe;
            session.Changed -= Refresh;
            overlay.MatchMarkingChanged -= Refresh;
            MatchView.Finish();
        };
        Refresh();
    }
}
