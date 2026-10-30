namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// When a meal document is sent, replaced or left alone (C-2.4.5, C-2.5.5; the meals counterpart of the workout redelivery rules). Pure.
///
/// <para><b>Only before the slot.</b> The slot is the sheet's deadline (after it, silence has approved the week and a new sheet would only confuse)
/// or the moment the cook block begins (the paper must not change under the cook's hand). A page already on the tablet is replaced only while
/// <paramref name="now"/> is strictly before <b>both</b> the slot it was sent for and the slot the plan now has: the owner reads the paper, so
/// the earlier of the two starts counts. Exactly the start counts as begun.</para>
///
/// <para><b>An unchanged page is left alone at any time</b>, so a run that comes late never reports "too late" for something already there.
/// A page not yet sent waits for <see cref="CurrentMealDocument.SendFrom"/> (a recipe's hour) and is never sent once the slot has begun.</para>
/// </summary>
public static class MealRedeliveryRules
{
    public static MealRedelivery Decide(DeliveredMealDocument? delivered, CurrentMealDocument current, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (delivered is not null)
        {
            if (string.Equals(delivered.Fingerprint, current.Fingerprint, StringComparison.Ordinal))
            {
                return MealRedelivery.LeaveAlone;
            }

            var begins = delivered.SlotStart < current.SlotStart ? delivered.SlotStart : current.SlotStart;
            return now < begins ? MealRedelivery.Replace : MealRedelivery.TooLate;
        }

        if (now >= current.SlotStart)
        {
            return MealRedelivery.TooLate;
        }

        return current.SendFrom is { } from && now < from ? MealRedelivery.NotYet : MealRedelivery.Send;
    }
}
