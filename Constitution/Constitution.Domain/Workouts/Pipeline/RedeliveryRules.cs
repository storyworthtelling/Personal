namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>What to do about one document this run (C-2.3.6).</summary>
public enum Redelivery
{
    /// <summary>No session, no document: nothing to do.</summary>
    Nothing,

    /// <summary>Not on the tablet yet: send it.</summary>
    Send,

    /// <summary>On the tablet, and the plan now says something else, and the slot has not begun: replace it under the same title.</summary>
    Replace,

    /// <summary>On the tablet and saying the same thing.</summary>
    LeaveAlone,

    /// <summary>It would be sent or replaced, but the slot has begun (or the session is over): nothing is sent.</summary>
    TooLate,

    /// <summary>On the tablet, and the session has been called off before its slot: take the page away.</summary>
    Withdraw,
}

/// <summary>What the plan says today about a document: what it would say (its fingerprint) and when its slot runs.</summary>
public sealed record CurrentWorkout(string Fingerprint, DateTimeOffset SlotStart, DateTimeOffset SlotEnd);

/// <summary>
/// C-2.3.6: a forecast, a recovery read or a sore knee can change a page after it has gone to the tablet. The page is
/// replaced silently, under the same title so the tablet shows one document, but <b>only before the slot</b>: once a
/// session has begun the paper must not change under the owner's hand, so nothing is sent, ever, for a session that has
/// begun.
///
/// <para><b>"Begun" is the earlier of two starts.</b> The paper on the tablet names the old start and the plan may now name
/// another; the owner reads the paper, so at or after the earlier of the two they may have started. A page is replaced only
/// while <paramref name="now"/> is strictly before both. Exactly the start counts as begun.</para>
///
/// <para><b>A session called off before its slot</b> (rain, soreness) has its page taken off the tablet
/// (<see cref="Redelivery.Withdraw"/>): left there, a blank page would be read the next morning as a skip the owner never made.
/// After the slot began it is left where it is.</para>
///
/// <para><b>A first send</b> is not a re-send: there is nothing on the tablet for it to change under. It goes whenever the
/// session has not yet ended, so a run that came late still gives the owner their paper.</para>
/// </summary>
public static class RedeliveryRules
{
    public static Redelivery Decide(DeliveredWorkout? delivered, CurrentWorkout? current, DateTimeOffset now)
    {
        if (delivered is null)
        {
            if (current is null)
            {
                return Redelivery.Nothing;
            }

            return now < current.SlotEnd ? Redelivery.Send : Redelivery.TooLate;
        }

        if (current is null)
        {
            return now < delivered.SlotStart ? Redelivery.Withdraw : Redelivery.TooLate;
        }

        if (string.Equals(delivered.Fingerprint, current.Fingerprint, StringComparison.Ordinal))
        {
            return Redelivery.LeaveAlone;
        }

        var begins = delivered.SlotStart < current.SlotStart ? delivered.SlotStart : current.SlotStart;
        return now < begins ? Redelivery.Replace : Redelivery.TooLate;
    }
}
