namespace Codex.Domain.Music;

/// <summary>
/// How many records are in the crate on a Friday.
///
/// It is a setting rather than a constant because how many new things fit in a week
/// is a fact about the owner, not about the app. It is clamped because nothing
/// carries over — an unfinished stack is replaced, not stacked on — so a size nobody
/// could get through in a week does not build a backlog, it just wastes most of the
/// run; and a stack of two is not a run at all.
/// </summary>
public static class FridayStack
{
    /// <summary>Below this it stops being a crate to flick through.</summary>
    public const int Smallest = 5;

    /// <summary>Above this the week ends long before the stack does.</summary>
    public const int Largest = 40;

    /// <summary>A fortnight's records for a week, which leaves room to skip some.</summary>
    public const int Default = 14;

    public static int Clamp(int size) => Math.Clamp(size, Smallest, Largest);
}
