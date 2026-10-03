using Index.Android.Views;
using Storyworthtelling.Maui.State;
using Storyworthtelling.Maui.Views;

namespace Index.Android.Pages;

/// <summary>
/// The dog-ear turned (mockup 17, D162): Waiting, drawn in Index's own frame — the area the fold belongs
/// to still lit in the binder strip, the graph paper behind, "For Accounts · newest first" in the status
/// line, and no system navigation bar (the base page hides it).
///
/// <para><b>Why a page of Index's own around the kit's <see cref="WaitingView"/>.</b> The shared kit
/// cannot see this app's area table or its tab-switch callback, so its freestanding
/// <see cref="WaitingPage"/> can only draw a header and rows on flat paper. Mockup 17 draws the Waiting
/// page as part of the area, so the kit supplies the body and this page supplies the frame:
/// <see cref="LedgerBinderPage.Wrap"/> puts the header, the rule and the strip around it, exactly as for
/// every other screen. Tapping the lit tab (or back) turns back to the area's root.</para>
///
/// <para>It is live: it listens to the page's <see cref="DogEarState"/> while it shows, so the last
/// notice resolving leaves "Nothing waiting." rather than a stale row.</para>
/// </summary>
public sealed class LedgerWaitingPage : LedgerBinderPage
{
    private readonly WaitingView body;

    public LedgerWaitingPage(LedgerArea area, DogEarState state)
    {
        var areaName = LedgerAreas.All[(int)area].Label;
        body = new WaitingView(state, $"For {areaName} · newest first");

        var ground = new Grid();
        ground.Children.Add(new GraphPaperBackground { GroundColour = Color.FromArgb(LedgerAreas.AccountsGroundHex) });
        ground.Children.Add(new ScrollView { Content = body });
        Content = ground;

        Wrap(area, "Waiting");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        body.Attach();
    }

    protected override void OnDisappearing()
    {
        body.Detach();
        base.OnDisappearing();
    }
}
