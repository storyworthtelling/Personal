using Constitution.Domain.Notices;

namespace Constitution.Settings;

/// <summary>
/// The one notice board of this process, the thing a dog-ear will read. Settings states the
/// connections to it each time it is shown (<c>SettingsModel.ApplyNotices</c>), so a broken one
/// waits and a mended one goes. The shell's <c>DogEarHost</c> reads it for the corner fold, and the Waiting page lists it.
/// </summary>
public static class ConnectionNoticeBoard
{
    private static readonly object Gate = new();
    private static readonly NoticeBoard Board = new();

    /// <summary>Runs <paramref name="use"/> on the board, one caller at a time.</summary>
    public static T With<T>(Func<NoticeBoard, T> use)
    {
        lock (Gate)
        {
            return use(Board);
        }
    }
}
