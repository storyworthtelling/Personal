namespace Constitution.Domain.Delivery;

/// <summary>The documents Constitution puts on the tablet. A closed list: a new one is a decision, because its name is read back by title.</summary>
public enum DeliveryKind
{
    /// <summary>One day's workout pages.</summary>
    Workout,

    /// <summary>The meal sheet.</summary>
    Meals,

    /// <summary>The weekly digest.</summary>
    Digest,
}
