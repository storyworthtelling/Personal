using Codex.Domain.Timeline;
using Watch.Domain.Dash;

namespace Codex.Infrastructure.Dash;

/// <summary>A file, a delete or a send from the wrist.</summary>
public enum DashActKind
{
    File,
    Delete,

    /// <summary>A text from the wrist (B2): kept so a send heard twice is carried out once.</summary>
    Send,
}

/// <summary>
/// One message an act took: what Post needs to take it back (a delete is held by kind and source id) and, should the
/// phone restart inside the window, to carry it out at the source (a mail is trashed at Gmail by its message id).
/// </summary>
public sealed record ActedItem(TimelineItemKind Kind, string SourceId, string? MessageId = null)
{
    /// <summary>The message as far as holding and deleting it needs: its kind, its source id and a mail's message id.</summary>
    public TimelineItem ToItem() =>
        new(Kind, ContactKey: string.Empty, DateTimeOffset.UnixEpoch, Snippet: null, Incoming: false, SourceId) { MessageId = MessageId };
}

/// <summary>
/// An act from the wrist: what it took, and until when it can be taken back. <paramref name="Taken"/>: an undo took it
/// back. <paramref name="Done"/>: a delete was carried out at the source. <paramref name="Sent"/>: how a send went, null
/// while it is being carried out (or when the process went before it said).
/// </summary>
public sealed record DashActRecord(
    string Act, DashActKind Kind, IReadOnlyList<ActedItem> Items, DateTimeOffset Until, bool Taken = false, bool Done = false,
    DashSendOutcome? Sent = null);

/// <summary>What an undo from the wrist takes back, or why it takes nothing.</summary>
public sealed record DashUndoPlan(DashOutcome Outcome, DashActKind Kind, IReadOnlyList<ActedItem> Items);

/// <summary>
/// The acts the wrist made in the last while, by the watch's own name for each, so an undo takes back exactly what its
/// act took: a whole correspondence filed from People waiting included, whose messages the watch never listed. An undo
/// after its act's window is too late, since Post may already have carried the act out at the source. Shared between
/// the Data Layer listener's threads, so every access is locked (as <see cref="DeleteBuffer"/> is).
///
/// <para>Kept on the phone across a restart (B2, <see cref="DashActsCodec"/>): a duplicated act heard after the process
/// came back is still done once, a send heard twice is answered as it went and never sent again, and a delete still
/// inside its window when the process went is still owed (<see cref="Owed"/>) until it is carried out (<see cref="Settle"/>).</para>
/// </summary>
public sealed class DashActs
{
    /// <summary>Acts kept, newest last; an older one is long past its window.</summary>
    public const int Kept = 64;

    private readonly object gate = new();
    private readonly List<DashActRecord> acts = [];

    public bool Knows(string act)
    {
        lock (gate)
        {
            return acts.Any(record => record.Act == act);
        }
    }

    public void Record(DashActRecord record)
    {
        lock (gate)
        {
            acts.RemoveAll(known => known.Act == record.Act);
            acts.Add(record);
            if (acts.Count > Kept)
            {
                acts.RemoveRange(0, acts.Count - Kept);
            }
        }
    }

    /// <summary>
    /// What an undo takes back. Inside the window, the act's messages, once: an undo heard twice takes nothing the second
    /// time. After the window, nothing: the act stands, and the wrist's ring had already said so; a delete not yet carried
    /// out is still owed. A send is never undone. An undo for an act not yet heard is kept, so that act is never done.
    /// </summary>
    public DashUndoPlan Undo(string act, DateTimeOffset now)
    {
        lock (gate)
        {
            var index = acts.FindIndex(record => record.Act == act);
            if (index < 0)
            {
                // Heard before its act (two messages can cross): kept as taken back, so the act that follows is known and
                // done never (B5). It takes nothing now, and is never owed.
                Record(new DashActRecord(act, DashActKind.File, [], now + DashLimits.MaxWindow, Taken: true));
                return new DashUndoPlan(DashOutcome.Unknown, DashActKind.File, []);
            }
            if (acts[index].Kind == DashActKind.Send)
            {
                return new DashUndoPlan(DashOutcome.Unknown, DashActKind.File, []);
            }

            var record = acts[index];
            if (record.Taken)
            {
                return new DashUndoPlan(DashOutcome.Again, record.Kind, []);
            }
            if (now > record.Until)
            {
                return new DashUndoPlan(DashOutcome.TooLate, record.Kind, []);
            }
            acts[index] = record with { Taken = true };
            return new DashUndoPlan(DashOutcome.TakenBack, record.Kind, record.Items);
        }
    }

    /// <summary>The deletes not taken back and not yet carried out at the source, oldest first.</summary>
    public IReadOnlyList<DashActRecord> Owed()
    {
        lock (gate)
        {
            return acts.Where(record => record.Kind == DashActKind.Delete && !record.Taken && !record.Done).ToList();
        }
    }

    /// <summary>The act was carried out at the source: it is no longer owed.</summary>
    public void Settle(string act) => Change(act, record => record with { Done = true });

    /// <summary>A send from the wrist is being carried out: heard again, it is not sent again.</summary>
    public void Sending(string act, DateTimeOffset now) => Record(new DashActRecord(act, DashActKind.Send, [], now));

    /// <summary>How the send went.</summary>
    public void Sent(string act, DashSendOutcome outcome) => Change(act, record => record with { Sent = outcome });

    /// <summary>
    /// How a send the wrist named went, for its answer when it is heard again: <see cref="DashSendOutcome.Failed"/> while it
    /// is being carried out or when the process went before it said, since Post cannot tell whether it went and never
    /// sends it twice. Null when no send has that name.
    /// </summary>
    public DashSendOutcome? SendAnswer(string act)
    {
        lock (gate)
        {
            return acts.FirstOrDefault(record => record.Act == act && record.Kind == DashActKind.Send) is { } send
                ? send.Sent ?? DashSendOutcome.Failed
                : null;
        }
    }

    public IReadOnlyList<DashActRecord> All
    {
        get
        {
            lock (gate)
            {
                return acts.ToList();
            }
        }
    }

    public static DashActs From(IEnumerable<DashActRecord> records)
    {
        var book = new DashActs();
        foreach (var record in records)
        {
            book.Record(record);
        }
        return book;
    }

    private void Change(string act, Func<DashActRecord, DashActRecord> change)
    {
        lock (gate)
        {
            var index = acts.FindIndex(record => record.Act == act);
            if (index >= 0)
            {
                acts[index] = change(acts[index]);
            }
        }
    }
}
