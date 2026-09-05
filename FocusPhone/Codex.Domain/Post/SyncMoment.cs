namespace Codex.Domain.Post;

/// <summary>Something the owner just did that the source needs to hear about.</summary>
public enum PostAction
{
    /// <summary>Filed: the ghost is kept and a delete is owed at the source.</summary>
    Filed,

    /// <summary>Deleted outright: gone here, and owed gone there.</summary>
    Deleted,

    /// <summary>
    /// A message went out — including the first one to somebody new, which is what
    /// "starting a correspondence" amounts to: it owes the source nothing until it is
    /// actually sent.
    /// </summary>
    Sent,
}

/// <summary>
/// When the sync that carries an action out to the source should run.
///
/// Post syncs on <b>every action</b>, not only when a screen opens or the half-hourly
/// worker wakes: filing that reaches Gmail some unpredictable time later reads as filing
/// that does not work, which is exactly what it looked like on the owner's phone.
///
/// The one rule that is not obvious is the wait. An <b>undoable</b> act must not reach the
/// source until its undo window has closed. Undo withdraws the tombstone, so a delete that
/// has not yet been attempted is simply cancelled — but one already carried out cannot be:
/// the message would return to Post and stay in the source's trash, and "undo" would be a
/// lie about everywhere except this phone. Sending has no undo, so it has nothing to wait
/// for.
/// </summary>
public static class SyncMoment
{
    public static TimeSpan Delay(PostAction action, TimeSpan undoWindow) => action switch
    {
        PostAction.Filed or PostAction.Deleted => undoWindow,
        _ => TimeSpan.Zero,
    };
}
