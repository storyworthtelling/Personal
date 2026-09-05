using Android.App;
using Android.Content;
using Android.OS;
using Codex.Domain.Design;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// Carrying an action out to the source, at the moment it should happen.
///
/// The owner's expectation, stated plainly: a sync happens every time they do something —
/// filing, replying, starting a correspondence — not only when a screen opens or the
/// half-hourly worker wakes. Filing that reached Gmail some unpredictable time later is
/// what made filing look broken on their phone (test plan P4-7).
///
/// <see cref="SyncMoment"/> decides the wait and is pure and tested. The one rule that is
/// not obvious: an <b>undoable</b> act waits for its undo window, because undo withdraws
/// the tombstone and a delete not yet attempted is simply cancelled — while one already
/// carried out cannot be.
///
/// Every sync here is best-effort and silent. A failure is already the Mailbox's to fold
/// into the corner on its next open; an action must never fail because the network did.
/// </summary>
internal static class Syncs
{
    private static readonly TimeSpan UndoWindow =
        TimeSpan.FromMilliseconds(DesignTokens.Motion.UndoWindowMs);

    // One at a time. Several actions in quick succession owe one round trip between them,
    // not one each, and the worker may be running anyway.
    private static int running;

    /// <summary>
    /// Syncs after <paramref name="action"/>, waiting out its undo window first where it
    /// has one. Returns immediately; nothing about the owner's action waits on this.
    /// </summary>
    public static void After(Activity host, PostAction action)
    {
        var delay = SyncMoment.Delay(action, UndoWindow);
        if (delay <= TimeSpan.Zero)
        {
            Run(host);
            return;
        }

        new Handler(Looper.MainLooper!).PostDelayed(() => Run(host), (long)delay.TotalMilliseconds);
    }

    private static void Run(Context context)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            return;
        }

        var app = context.ApplicationContext ?? context;
        Task.Run(async () =>
        {
            try
            {
                await PostSync.RunReconcileAsync(app);
            }
            catch (Exception e)
            {
                // Deliberately quiet. The owner already did the thing they wanted; the
                // Mailbox reports a sync failure on its own next open, and the owed delete
                // is retried by every sync after this one.
                Android.Util.Log.Warn("Post", $"Sync after an action failed: {e}");
                PostDiagnostics.LogWarning("Sync", "Sync after an action failed", e);
            }
            finally
            {
                Interlocked.Exchange(ref running, 0);
            }
        });
    }
}
