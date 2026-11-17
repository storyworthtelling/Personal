using Android.App;
using Android.Views;
using Constitution.Domain.Meals.Screens;
using Constitution.MealsTab;
using Constitution.Domain.Today;
using Constitution.Domain.Voice;
using Constitution.Today;
using Constitution.WorkoutsTab;
using Storyworthtelling.Materials;

namespace Constitution;

/// <summary>
/// The Today tab (C-4.1). At Release A no plan exists, so the page shows the neutral
/// top line (the date is the header's subtitle) on a taped notepad leaf; give it a <see cref="Plan"/> later and the same page shows the
/// seat's own sentence, with no rework. Its single action is the pinned bar's "I ate something else",
/// whose estimate appears on the meal's card (book p.15).
/// </summary>
public sealed class TodayArea(Activity host)
    : ConstitutionAreaBase(host, "today", TabGlyphs.Page), IUndoableArea
{
    private readonly WorkoutsLog log = new(host);
    private TodayPlan? plan;
    private IMealWeekSource mealWeek = new NoMealWeekSource();
    private AteSomethingElseFlow? ate;

    // Today's voiced top line (audit W10.4): the seat's request it answered, and the line. Hidden until composed.
    private SeatRequest? voicedFor;
    private string? voicedLine;

    /// <summary>Where tonight's dinner comes from; set by whoever wires the weekly meal run's saved week.</summary>
    public IMealWeekSource MealWeek
    {
        get => mealWeek;
        set => mealWeek = value;
    }

    /// <summary>The "I ate something else" flow the bar opens and whose lines sit on the cards; shared with Meals.</summary>
    public AteSomethingElseFlow? Ate
    {
        get => ate;
        set
        {
            ate = value;
            if (value is not null)
            {
                value.Changed += RaiseChanged;
            }
        }
    }

    public void UndoTurned(string actKey) => ate?.UndoTurned(actKey);

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
        ate?.LoadOnce();
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

    protected override View BuildPage()
    {
        var topLine = TodayModel.TopLine(plan);
        if (topLine.Request is { } request)
        {
            if (SameFacts(request, voicedFor))
            {
                topLine = topLine with { Text = voicedLine };
            }
            else
            {
                Compose(request);
            }
        }

        var day = DateOnly.FromDateTime(DateTime.Now);
        return new TodayPage(Host, topLine, TodayModel.Clipping(plan), TodayModel.Folio(day), TodayModel.Dinner(plan), AteCards.Lines(ate, day), AteCards.Status(ate, day));
    }

    // The top line (today's or a rest day's) in the owner's voice, by the suite's one rule through the shared seat
    // composer: the page draws without it and redraws once it arrives; with no voice it stays hidden.
    private void Compose(SeatRequest request)
    {
        voicedFor = request;
        voicedLine = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var line = await Constitution.BodyTab.BodySeats.ComposeAsync(Host, request, CancellationToken.None);
                if (line is null)
                {
                    return;
                }

                Host.RunOnUiThread(() =>
                {
                    if (SameFacts(request, voicedFor))
                    {
                        voicedLine = line.Text;
                        RaiseChanged();
                    }
                });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.Outcome($"today voice: {e.GetType().Name}");
            }
        });
    }

    private static bool SameFacts(SeatRequest request, SeatRequest? other) =>
        other is not null && other.Seat == request.Seat && other.Facts.SequenceEqual(request.Facts);

    public override View? BuildBottomBar() => TodayActionBar.Build(Host, ate);
}
