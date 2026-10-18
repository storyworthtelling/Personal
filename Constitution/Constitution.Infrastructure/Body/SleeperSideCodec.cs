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
}
