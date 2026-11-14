using Storyworthtelling.Folds;
using Storyworthtelling.Maui.Views;

namespace Foreword.Android.Pages;

/// <summary>
/// The dog-ear turned: what is waiting, drawn in Foreword's own frame — the area the fold belongs to still
/// lit in the strip — as Index's Waiting page is (audit W8.3a, report 14 §8: Foreword opened the kit's bare
/// page, with no binder). The kit supplies the body; this page supplies the frame.
/// </summary>
public sealed class ForewordWaitingPage : BinderPage
{
    private readonly WaitingView body;

    public ForewordWaitingPage(ForewordArea area, DogEarState state)
    {
        body = new WaitingView(state, $"For {ForewordAreas.All[(int)area].Label} · newest first");
        Content = new ScrollView { Content = body };
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
