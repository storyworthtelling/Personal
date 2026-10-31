using Constitution.Domain.Notices;

namespace Constitution.Domain.ReadBack;

/// <summary>The one notice the read-back raises by itself (R3). Questions about a mark go through <see cref="Ink.InkAsks"/>.</summary>
public static class ReadBackNotices
{
    /// <summary>The cause the notice is filed under, so raising it again changes nothing and a mark takes it away.</summary>
    public const string NoPenMarksCause = "ink:no-pen-marks";

    /// <summary>States that a page came back with no pen marks and none ever has. Returns whether it was newly raised.</summary>
    public static bool NoPenMarks(NoticeBoard board, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.Raise(NoticeKind.NoPenMarks, NoPenMarksCause, now);
    }

    /// <summary>A mark has come through, so the cause is gone. Returns whether a notice was waiting.</summary>
    public static bool PenMarksSeen(NoticeBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.Resolve(NoPenMarksCause);
    }
}
