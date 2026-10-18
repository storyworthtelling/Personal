namespace Constitution.Domain.Documents;

public enum InkTargetKind
{
    LargeTick,
    StandardTick,
    CircleLane,
    NumberBox,
    NoBecause,
    NotesMargin,
}

/// <summary>
/// One place a pen can answer: where it is, what a mark there means, and (for a number) the unit
/// printed outside it. The painter draws it and the reader looks it up, from this one instance.
/// </summary>
/// <param name="Group">Targets of one group touch by design (circle lanes); null for a target on its own.</param>
public sealed record InkTarget(
    string Id, InkTargetKind Kind, InkRect Bounds, string Meaning, string? Unit = null, string? Group = null)
{
    /// <summary>Only a lane (its word) and the notes margin (its rules) are printed in on purpose.</summary>
    public bool AllowsOwnPrintInside => Kind is InkTargetKind.CircleLane or InkTargetKind.NotesMargin;

    /// <summary>What the reader is told: "box3 is push-ups, in reps".</summary>
    public string Describe() => Unit is null ? $"{Id} is {Meaning}" : $"{Id} is {Meaning}, in {Unit}";

    /// <summary>Refuses a blank id or a blank meaning: a target nobody can name is a target nobody can read.</summary>
    internal static void Require(string id, string meaning, string what)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException($"A {what} needs an id so the reader can ask for it by name.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(meaning))
        {
            throw new ArgumentException(
                $"The {what} '{id}' needs a few words for what a mark there means; a box with no words cannot be read.",
                nameof(meaning));
        }
    }
}
