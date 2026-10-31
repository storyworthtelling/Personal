using Android.App;
using Android.Views;
using Codex.Ui;
using Constitution.Domain.Today;
using Constitution.MealsTab;

namespace Constitution.Today;

/// <summary>
/// Today's pinned bottom bar: the page's single action, "I ate something else" (C-4.1.4, book p.11). It opens the one camera of
/// <see cref="AteSomethingElseFlow"/> and nothing else: the answer arrives on the meal's card (book p.15). The button waits while a
/// photo is being taken or estimated, and is drawn disabled when no flow is wired. This file is the only one in Today allowed a
/// listener, and <c>TodayHasOneActionTests</c> allows it one.
/// </summary>
public static class TodayActionBar
{
    public static View Build(Activity host, AteSomethingElseFlow? flow)
    {
        var button = PaperUi.SecondaryButton(host, TodayModel.ActionLabel, () => flow?.Start());
        button.Enabled = flow is { CanStart: true };
        return PaperUi.BottomBar(host, button);
    }
}
