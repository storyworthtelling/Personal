using Codex.Domain.Timeline;
using Watch.Domain.Dash;

namespace Codex.Infrastructure.Dash;

/// <summary>A file or a delete from the wrist.</summary>
public enum DashActKind
{
    File,
    Delete,
}

/// <summary>One message an act took: what Post needs to take it back (a delete is held by kind and source id).</summary>
public sealed record ActedItem(TimelineItemKind Kind, string SourceId);

/// <summary>An act from the wrist: what it took, and until when it can be taken back.</summary>
public sealed record DashActRecord(string Act, DashActKind Kind, IReadOnlyList<ActedItem> Items, DateTimeOffset Until, bool Taken = false);

/// <summary>What an undo from the wrist takes back, or why it takes nothing.</summary>
public sealed record DashUndoPlan(DashOutcome Outcome, DashActKind Kind, IReadOnlyList<ActedItem> Items);

/// <summary>
/// The acts the wrist made in the last while, by the watch's own name for each, so an undo takes back exactly what its
/// act took: a whole correspondence filed from People waiting included, whose messages the watch never listed. An undo
/// after its act's window is too late, since Post may already have carried the act out at the source. Shared between
/// the Data Layer listener's threads, so every access is locked (as <see cref="DeleteBuffer"/> is).
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
    /// time. After the window, nothing: the act stands, and the wrist's ring had already said so.
    /// </summary>
    public DashUndoPlan Undo(string act, DateTimeOffset now)
    {
        lock (gate)
        {
            var index = acts.FindIndex(record => record.Act == act);
            if (index < 0)
            {
                return new DashUndoPlan(DashOutcome.Unknown, DashActKind.File, []);
            }

            var record = acts[index];
            if (record.Taken)
            {
                return new DashUndoPlan(DashOutcome.Again, record.Kind, []);
            }
            acts[index] = record with { Taken = true };
            return now > record.Until
                ? new DashUndoPlan(DashOutcome.TooLate, record.Kind, [])
                : new DashUndoPlan(DashOutcome.TakenBack, record.Kind, record.Items);
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
}
