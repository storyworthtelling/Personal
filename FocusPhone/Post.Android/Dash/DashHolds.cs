using Android.Content;
using Android.OS;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Dash;
using Codex.Ui;
using Watch.Domain;
using Watch.Domain.Dash;

namespace Post;

/// <summary>
/// A delete from the wrist, undo-first as on the phone (P14 B2): its messages are held in the process's one delete buffer
/// (<see cref="SourceDeletes.Held"/>), so they are gone from the Mailbox and from what the wrist is shown at once, and
/// when the window Post holds the act for closes, whatever an undo did not let go is deleted at the source by the
/// Mailbox's own code (<see cref="SourceDeletes.Commit"/>) and the act is settled in the book.
///
/// <para>The buffer and the timer live in the process; the book outlives it. So at every start of the process
/// (<see cref="Resume"/>, from <c>PostApplication</c>) each delete the book still owes is held again for what is left of
/// its window, or carried out at once if the window has closed: a delete from the wrist is never lost to a restart, and
/// never carried out inside its window.</para>
/// </summary>
internal static class DashHolds
{
    private const string Tag = "Dash";  // not copy: a log tag

    private static int resumed;

    /// <summary>Holds <paramref name="items"/> until <paramref name="until"/>, then deletes them at the source.</summary>
    public static void Hold(Context context, string act, IReadOnlyList<TimelineItem> items, DateTimeOffset until)
    {
        var app = context.ApplicationContext ?? context;
        var now = DateTimeOffset.UtcNow;
        var left = until > now ? until - now : TimeSpan.Zero;
        foreach (var item in items)
        {
            SourceDeletes.Held.Schedule(item, now, left);
        }

        // The main looper keeps the time; the commit itself (database, provider and network) runs off it.
        new Handler(Looper.MainLooper!).PostDelayed(() => Task.Run(() => Commit(app, act, items)), (long)left.TotalMilliseconds);
    }

    /// <summary>
    /// Once a process: every delete the book still owes is held again, or carried out if its window has closed. Off the
    /// main thread: it reads the preferences.
    /// </summary>
    public static void Resume(Context context)
    {
        if (Interlocked.Exchange(ref resumed, 1) != 0)
        {
            return;
        }

        var owed = DashBook.Acts(context).Owed();
        foreach (var record in owed)
        {
            Hold(context, record.Act, record.Items.Select(item => item.ToItem()).ToList(), record.Until);
        }
        if (owed.Count > 0)
        {
            HeadLog.Info(Tag, DashLogLines.Outcome(WatchIdentity.DashDeletePath, DashOutcome.Held, owed.Count));
        }
    }

    private static void Commit(Context context, string act, IReadOnlyList<TimelineItem> items)
    {
        try
        {
            foreach (var item in items)
            {
                // Only what an undo did not let go in the meantime.
                if (SourceDeletes.Held.Undo(item.Kind, item.SourceId))
                {
                    SourceDeletes.Commit(context, item);
                }
            }

            DashBook.Acts(context).Settle(act);
            DashBook.Save(context);
        }
        catch (Exception e)
        {
            // Nothing is thrown into the looper: the book still owes what was not settled, and the next start carries it out.
            HeadLog.Quiet(Tag, "A delete from the wrist stopped; the next start carries it out", e);
        }
    }
}
