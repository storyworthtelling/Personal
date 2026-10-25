using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Time;
using Constitution.Domain.Voice;
using Constitution.Domain.Workouts.Screens;

namespace Constitution.Domain.Today;

/// <summary>Today's workout as its clipping says it (C-4.1.2): "Workout · 5:30", "Strength, 40 minutes", "Score 132 bpm · Steady".</summary>
public sealed record TodayClipping(string Kicker, string Headline, string? ScoreLine);

/// <summary>
/// The seat at the top of Today: its sentence, which of the six seats it is, whether it is in the
/// owner's voice, and the request a composer would be handed (null when there is no plan to compose from).
/// </summary>
public sealed record TodayTopLine(string Text, VoiceSeat Seat, bool IsVoiced, SeatRequest? Request);

/// <summary>
/// Everything Today decides that is not drawing (C-4.1.1, C-4.1.4, book p.11 and p.18). The line is
/// never hidden and never empty: with a plan it is the seat's own plain fallback (the rest-day seat
/// on a rest day); without one it is the neutral sentence. It is unvoiced here, because voicing is a
/// composer's answer (<c>SeatComposer</c>) arriving once a plan exists to compose from.
/// </summary>
public static class TodayModel
{
    public const string NeutralLine = "Nothing is planned yet.";

    /// <summary>The page's one action.</summary>
    public const string ActionLabel = "I ate something else";

    /// <summary>True from Release D: the action opens the photo flow (F-6.6).</summary>
    public const bool ActionIsLive = true;

    public static TodayTopLine TopLine(TodayPlan? plan)
    {
        if (plan?.Dinner is not { } dinner)
        {
            return new TodayTopLine(NeutralLine, VoiceSeat.TodayTopLine, false, null);
        }

        var request = plan.IsRestDay
            ? SeatFacts.RestDay(new RestDayFacts(dinner))
            : SeatFacts.Today(new TodayFacts(plan.WorkoutStart!.Value, plan.WorkoutKind!, plan.WorkoutMinutes!.Value, dinner));
        return new TodayTopLine(request.Fallback, request.Seat, false, request);
    }

    /// <summary>The workout clipping, or null on a rest day or when nothing is planned: an absent clipping is the rest day's whole signal.</summary>
    public static TodayClipping? Clipping(TodayPlan? plan)
    {
        if (plan is null || plan.IsRestDay)
        {
            return null;
        }

        return new TodayClipping(
            $"Workout · {SeatWords.Clock(plan.WorkoutStart!.Value)}",
            $"{SeatWords.Sentence(plan.WorkoutKind!)}, {plan.WorkoutMinutes} minutes",
            plan.ScoreBpm is { } bpm ? WorkoutWords.Score(bpm) : null);
    }

    /// <summary>Tonight's dinner card or receipt, or null when the meal week has nothing for tonight (C-4.1.3).</summary>
    public static DinnerClipping? Dinner(TodayPlan? plan) => plan?.DinnerCard;

    /// <summary>The magazine page number a clipping prints in its corner, steady for a given day.</summary>
    public static string Folio(DateOnly day) => $"— {2 + (Math.Abs(day.DayNumber) % 98):00} —";

    public static string Header(DateTime moment) => TodayHeader.Date(moment);
}
