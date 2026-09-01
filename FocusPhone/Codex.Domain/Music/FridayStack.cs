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

    /// <summary>The step between the sizes the picker offers.</summary>
    private const int Step = 5;

    /// <summary>
    /// The sizes the settings picker offers, given the size currently in force.
    ///
    /// Round numbers are what anyone actually wants to pick, so the ladder is
    /// <see cref="Smallest"/> to <see cref="Largest"/> in fives. But the size in force
    /// need not be one of them — <see cref="Default"/> is 14, and an older or hand-set
    /// value could be anything — and a picker that cannot show the value it is editing
    /// has no honest row to open on. So whatever is in force is always on the ladder,
    /// in its sorted place, and it drops off again the moment a round number is chosen.
    /// </summary>
    public static IReadOnlyList<int> Ladder(int inForce)
    {
        var rungs = new List<int>();
        for (var size = Smallest; size <= Largest; size += Step)
        {
            rungs.Add(size);
        }

        // Clamped first: a size outside the range was never choosable, so offering it
        // would put a value on the ladder that Clamp would immediately overrule.
        var current = Clamp(inForce);
        if (!rungs.Contains(current))
        {
            rungs.Add(current);
            rungs.Sort();
        }

        return rungs;
    }

    /// <summary>
    /// Where the size in force sits on <see cref="Ladder"/>. Always a real row —
    /// the ladder is built to contain it — so the caller never has a missing-index
    /// case to guess a fallback for.
    /// </summary>
    public static int IndexOn(int inForce)
    {
        var current = Clamp(inForce);
        var index = Ladder(current).ToList().IndexOf(current);

        // Unreachable by construction; kept because a silent -1 is exactly the bug
        // this pair of methods exists to end.
        return index < 0
            ? throw new InvalidOperationException($"Crate size {current} is missing from its own ladder.")
            : index;
    }
}
