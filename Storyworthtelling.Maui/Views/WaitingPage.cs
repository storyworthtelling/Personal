using Microsoft.Maui.Graphics;
using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// The dog-ear's notice fold turned into a page (DESIGN.md §13, D162): a Serif header, the rule, an
/// optional status line, and every notice written straight onto the page — no cards, no count. This is
/// what <see cref="DogEar.NoticesOpened"/> had nothing to open into before.
///
/// <para><b>The frameless host.</b> A plain, freestanding <see cref="ContentPage"/> so any host can
/// push it — <c>await Navigation.PushAsync(new WaitingPage(state, subtitle))</c> is the caller's job. It
/// does not wear the calling app's binder tabs or its graph paper: each app's page shape (e.g.
/// <c>FolioBinderPage</c>, <c>LedgerBinderPage</c>) lives in that app's own project and wraps a screen
/// with its own area table and tab-switch callback, which this shared kit cannot see. An app that wants
/// mockup 17's frame — the area's tab still lit, the graph paper behind — puts the body,
/// <see cref="WaitingView"/>, inside its own binder page instead (Index's <c>LedgerWaitingPage</c>); this
/// page is for an app that has not adopted one.</para>
///
/// <para>It hides the system navigation bar, as every binder page in the suite does: the header
/// <i>is</i> the header, and the default bar was drawing a second one above "Waiting".</para>
///
/// <para>Each notice prints its date (when it has one) and the list is already newest-first; both come
/// from <see cref="DogEarState"/> and <see cref="WaitingPlan"/>, not from this file. Given the state it
/// is live — see <see cref="WaitingView"/>.</para>
/// </summary>
public sealed class WaitingPage : ContentPage
{
    private readonly WaitingView body;

    /// <summary>A live page over <paramref name="state"/>: it empties to "Nothing waiting." when the
    /// last notice resolves while it is open.</summary>
    public WaitingPage(DogEarState state, string? subtitle = null, Color? ruleInk = null)
        : this(new WaitingView(state, subtitle), ruleInk)
    {
    }

    /// <summary>A page of a fixed list, drawn once.</summary>
    public WaitingPage(IReadOnlyList<DogEarNotice> notices, string? subtitle = null, Color? ruleInk = null)
        : this(new WaitingView(notices, subtitle), ruleInk)
    {
    }

    private WaitingPage(WaitingView body, Color? ruleInk)
    {
        this.body = body;
        NavigationPage.SetHasNavigationBar(this, false);

        var header = new PaperPageHeader
        {
            Text = "Waiting",
            Ink = ruleInk ?? PaperResources.Colour("Ink"),
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout { Children = { header, body } },
        };

        BackgroundColor = PaperResources.Colour("Paper");
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
