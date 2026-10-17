using Android.App;
using Android.Views;
using Codex.Ui;
using Constitution.Domain.Today;

namespace Constitution.Today;

/// <summary>
/// Today's pinned bottom bar: the page's single action, "I ate something else" (C-4.1.4, book p.11).
/// The action itself is built in Release D (F-6.6); until then the button is drawn disabled and does
/// nothing, so there is no handler to write. This file is the only one in Today allowed a listener,
/// and <c>TodayHasOneActionTests</c> allows it one.
/// </summary>
public static class TodayActionBar
{
    public static View Build(Activity host)
    {
        var button = PaperUi.SecondaryButton(host, TodayModel.ActionLabel, () => { });
        button.Enabled = TodayModel.ActionIsLive;
        return PaperUi.BottomBar(host, button);
    }
}
