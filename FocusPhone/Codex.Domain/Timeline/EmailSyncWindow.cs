namespace Codex.Domain.Timeline;

// Chooses how far back an email sync fetches from Gmail. A reconcile always mirrors the full window so the
// snapshot is complete and server-side deletions drop out; an incremental sync resumes from the last
// successful sync, falling back to the full window on the first run (no prior success) or an unparseable
// stored timestamp. Pure so the windowing is unit-tested without a device.
public static class EmailSyncWindow
{
    public static DateTimeOffset Since(bool reconcile, DateTimeOffset? lastSync, DateTimeOffset now, TimeSpan window) =>
        reconcile || lastSync is null ? now - window : lastSync.Value;
}
