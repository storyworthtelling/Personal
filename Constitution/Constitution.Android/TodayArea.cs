using Android.App;
using Android.Views;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Today;
using Constitution.Today;
using Constitution.WorkoutsTab;

namespace Constitution;

/// <summary>
/// The Today tab (C-4.1). At Release A no plan exists, so the page shows the neutral
/// top line (the date is the header's subtitle) on a taped notepad leaf; give it a <see cref="Plan"/> later and the same page shows the
/// seat's own sentence, with no rework. Its single action is the pinned bar's "I ate something else",
/// inert until Release D.
/// </summary>
public sealed class TodayArea(Activity host)
    : ConstitutionAreaBase(host, "today", Resource.Drawable.areatoday)
{
    private readonly WorkoutsLog log = new(host);
    private TodayPlan? plan;
    private IMealWeekSource mealWeek = new NoMealWeekSource();

    /// <summary>Where tonight's dinner comes from; set by whoever wires the weekly meal run's saved week.</summary>
    public IMealWeekSource MealWeek
    {
        get => mealWeek;
        set => mealWeek = value;
    }

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

    public override void Resume()
    {
        // The saved week is read off the UI thread; the page keeps what it had if the read fails, and the trail names only the type.
        _ = Task.Run(() =>
        {
            TodayPlan? fresh;
            try
            {
                fresh = WorkoutsTabServices.TodayOf(Host);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                log.Outcome($"today read failed: {e.GetType().Name}");
                return;
            }

            fresh = WithDinner(fresh);
            Host.RunOnUiThread(() =>
            {
                if (fresh != plan)
                {
                    Plan = fresh;
                }
            });
        });
    }

    // Tonight's dinner is read from the meal week and laid on the day's plan. With no workout plan read, the day is unknown rather
    // than a rest day, so only the card is carried and the top line stays neutral.
    private TodayPlan? WithDinner(TodayPlan? workouts)
    {
        MealWeekScreenRead week;
        try
        {
            week = mealWeek.Read(DateTimeOffset.Now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log.Outcome($"meal week read failed: {e.GetType().Name}");
            return workouts;
        }

        var day = DateOnly.FromDateTime(DateTime.Now);
        var card = DinnerClipping.For(week, day);
        if (card is null)
        {
            return workouts;
        }

        return workouts is null
            ? new TodayPlan(null, null, null, null) { DinnerCard = card }
            : workouts.WithDinner(DinnerClipping.PlanOf(week, day), card);
    }

    public override string? Subtitle => TodayModel.Header(DateTime.Now);

    protected override View BuildPage() => new TodayPage(Host, TodayModel.TopLine(plan), TodayModel.Clipping(plan), TodayModel.Folio(DateOnly.FromDateTime(DateTime.Now)), TodayModel.Dinner(plan));

    public override View? BuildBottomBar() => TodayActionBar.Build(Host);
}
