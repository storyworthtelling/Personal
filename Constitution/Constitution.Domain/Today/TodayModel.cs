using Constitution.Domain.Time;
using Constitution.Domain.Voice;

namespace Constitution.Domain.Today;

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

    /// <summary>The page's one action; built in Release D, inert before it.</summary>
    public const string ActionLabel = "I ate something else";

    /// <summary>False until Release D builds the action (F-6.6).</summary>
    public const bool ActionIsLive = false;

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

    public static string Header(DateTime moment) => TodayHeader.Date(moment);
}
