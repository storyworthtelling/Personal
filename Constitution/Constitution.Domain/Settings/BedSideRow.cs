namespace Constitution.Domain.Settings;

/// <summary>
/// The Bed side row in Settings, Connections (review S7): which side of the household's bed is this person's. The
/// login is shared, so this is the one thing that says whose night to read; until it is chosen the bed is not used at
/// all and the watch is read alone. Left, Right, or None. Shown only once a Sleep Number login is set, since without
/// one there is nothing to choose a side of.
/// </summary>
/// <param name="Name">The row's name.</param>
/// <param name="Labels">The segments, in order.</param>
/// <param name="Selected">The segment lit: None while nothing has been chosen.</param>
/// <param name="Caption">The muted sentence under the name.</param>
public sealed record BedSideRow(string Name, IReadOnlyList<string> Labels, int Selected, string Caption)
{
    private static readonly string[] Words = ["left", "right", "none"];

    /// <summary>The row, or null when no login is set.</summary>
    /// <param name="loginSet">Whether both halves of the Sleep Number login are held.</param>
    /// <param name="stored">The stored word: <c>left</c>, <c>right</c>, <c>none</c>, or empty when never chosen.</param>
    public static BedSideRow? Build(bool loginSet, string stored)
    {
        if (!loginSet)
        {
            return null;
        }

        var selected = Array.FindIndex(Words, word => string.Equals(word, stored?.Trim(), StringComparison.OrdinalIgnoreCase));
        return new BedSideRow(
            "Bed side",
            ["Left", "Right", "None"],
            selected < 0 ? Words.Length - 1 : selected,
            "Which side of the bed is yours. It fills in the nights your watch missed.");
    }

    /// <summary>The word to store for a segment.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is not one of the three segments.</exception>
    public static string StoredFor(int index) =>
        index >= 0 && index < Words.Length
            ? Words[index]
            : throw new ArgumentOutOfRangeException(nameof(index), index, "That is not one of the three bed-side segments.");
}
