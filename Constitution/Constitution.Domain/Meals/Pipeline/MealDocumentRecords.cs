using Constitution.Domain.Delivery;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// One meal document as it went to the tablet: its title, whose tablet and which day, when its slot begins and a fingerprint of what it
/// said. It is what lets a later run tell "already there, same", "already there, changed, still before the slot" and "too late" apart.
/// It holds a title, a name, a day, a time and a hash: never a line of the page.
/// </summary>
/// <param name="SlotStart">The sheet's deadline, or the moment the cook block begins.</param>
public sealed record DeliveredMealDocument(string Name, DeliveryKind Kind, string Person, DateOnly Day, DateTimeOffset SlotStart, string Fingerprint);

/// <summary>The record of what has been delivered. Pruning old entries is the implementation's business.</summary>
public interface IDeliveredMealDocuments
{
    DeliveredMealDocument? Find(string name);

    void Record(DeliveredMealDocument delivered);

    void Remove(string name);
}

/// <summary>What the plan says now about a document: what it would say, when its slot begins, and the earliest it may be sent (null for no wait).</summary>
public sealed record CurrentMealDocument(string Fingerprint, DateTimeOffset SlotStart, DateTimeOffset? SendFrom);

/// <summary>What to do about one meal document this run.</summary>
public enum MealRedelivery
{
    /// <summary>Not on the tablet yet, and it is time: send it.</summary>
    Send,

    /// <summary>On the tablet, the plan now says something else, and the slot has not begun: replace it under the same title.</summary>
    Replace,

    /// <summary>On the tablet and saying the same thing.</summary>
    LeaveAlone,

    /// <summary>Not on the tablet yet and not yet the hour: nothing is sent now.</summary>
    NotYet,

    /// <summary>It would be sent or replaced, but the slot has begun: nothing is sent, ever.</summary>
    TooLate,
}
