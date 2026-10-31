using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Infrastructure.Body;

/// <summary>
/// How "which side of the bed is mine" is stored: the word <c>left</c> or <c>right</c>, or nothing. Anything else
/// is nothing chosen; a side is never guessed, because the wrong side is somebody else's night.
/// </summary>
public static class SleeperSideCodec
{
    public static BedSide? Read(string? stored) => stored?.Trim().ToLowerInvariant() switch
    {
        "left" => BedSide.Left,
        "right" => BedSide.Right,
        _ => null,
    };

    public static string Write(BedSide? side) => side switch
    {
        BedSide.Left => "left",
        BedSide.Right => "right",
        _ => string.Empty,
    };

    /// <summary>
    /// A side the person chose, or <c>none</c> when they chose not to use the bed. Unlike <see cref="Write"/>, which
    /// leaves nothing chosen blank, this records a decision, so a restore from the vault (which fills only blanks)
    /// cannot hand the old side back.
    /// </summary>
    public static string WriteChoice(BedSide? side) => side is null ? "none" : Write(side);
}
