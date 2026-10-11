using Android.App;
using Android.Views;
using Pulp.Domain.ChildView;
using Pulp.Domain.Shell;
using Pulp.Sports;

namespace Pulp.Shell;

/// <summary>
/// Sports, one tab of Pulp's binder (mockup 01, D212; the leagues are mockup 07). The area keeps the
/// frame's contract — header, Back, Resume — and hands everything else to <see cref="SportsScreen"/>.
/// A child never sees this tab (D231, <c>ChildViewAreas</c>), so no child page exists here.
/// </summary>
public sealed class SportsArea : PulpAreaPage
{
    private readonly SportsScreen screen;

    public SportsArea(Activity host) : base(host, PulpArea.Sports)
    {
        screen = new SportsScreen(host, () => Overlay, Invalidate);
    }

    /// <summary>A league's page names itself (Standings, Playoff bracket, Schedule); the list is the area's.</summary>
    public override string Title => screen.Title ?? Spec.Title;

    public override View BuildView() => screen.Build();

    public override bool GoBack() => screen.GoBack();

    public override void Resume() => screen.Reload();

    public override void Pause() => screen.Pause();
}
