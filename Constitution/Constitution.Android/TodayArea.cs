using Android.App;
using Android.Views;
using Constitution.Domain.Today;
using Constitution.Today;

namespace Constitution;

/// <summary>
/// The Today tab (C-4.1). At Release A no plan exists, so the page shows the date and the neutral
/// top line on a taped notepad leaf; give it a <see cref="Plan"/> later and the same page shows the
/// seat's own sentence, with no rework. Its single action is the pinned bar's "I ate something else",
/// inert until Release D.
/// </summary>
public sealed class TodayArea(Activity host)
    : ConstitutionAreaBase(host, "today", Resource.Drawable.areatoday)
{
    private TodayPlan? plan;

    /// <summary>Today's plan, or null while none exists. Setting it redraws the page.</summary>
    public TodayPlan? Plan
    {
        get => plan;
        set
        {
            plan = value;
            RaiseChanged();
        }
    }

    protected override View BuildPage() => new TodayPage(Host, DateTime.Now, TodayModel.TopLine(plan));

    public override View? BuildBottomBar() => TodayActionBar.Build(Host);
}
