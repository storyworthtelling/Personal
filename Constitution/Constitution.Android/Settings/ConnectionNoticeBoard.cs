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

    private static Action<IReadOnlyList<Notice>>? observer;

    /// <summary>
    /// Sets the one watcher told what is waiting after every use of the board (the notification poster, C-9.1.3).
    /// Null removes it. It is called outside the lock, with a snapshot, so it can do slow work.
    /// </summary>
    public static void Observe(Action<IReadOnlyList<Notice>>? watcher)
    {
        lock (Gate)
        {
            observer = watcher;
        }
    }

    /// <summary>Runs <paramref name="use"/> on the board, one caller at a time.</summary>
    public static T With<T>(Func<NoticeBoard, T> use)
    {
        T result;
        IReadOnlyList<Notice>? snapshot = null;
        Action<IReadOnlyList<Notice>>? watcher;
        lock (Gate)
        {
            result = use(Board);
            watcher = observer;
            if (watcher is not null)
            {
                snapshot = Board.Waiting();
            }
        }

        if (watcher is not null && snapshot is not null)
        {
            watcher(snapshot);
        }

        return result;
    }
}
