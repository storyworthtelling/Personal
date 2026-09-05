using System.Collections.Immutable;

namespace Codex.Domain.Plate;

/// <summary>What kind of thing is waiting.</summary>
public enum DarkroomKind
{
    /// <summary>A picture the owner took, waiting to be chosen or thrown away.</summary>
    Photo,

    /// <summary>A scan that has been handed over but not confirmed as arrived.</summary>
    Scan,
}

/// <summary>
/// One thing waiting in the Darkroom.
///
/// <para>Two shapes in one room, deliberately: pictures to choose between and the scan whose
/// handoff has not landed are both "not yet gone", and splitting them into two rooms would
/// mean the owner has to remember which one to check.</para>
/// </summary>
public sealed record DarkroomItem
{
    private DarkroomItem(string id, DarkroomKind kind, DateTime capturedAt)
    {
        Id = id;
        Kind = kind;
        CapturedAt = capturedAt;
    }

    /// <summary>
    /// A picture. <paramref name="id"/> is the private file it was captured to — the
    /// Darkroom holds the only copy until the owner sends it, which is the cost the owner
    /// accepted knowingly when they reversed the spine.
    /// </summary>
    public static DarkroomItem Photo(string id, DateTime capturedAt) =>
        new(id, DarkroomKind.Photo, capturedAt);

    /// <summary>A scan waiting on a handoff nobody has confirmed.</summary>
    public static DarkroomItem Scan(
        string id, DateTime capturedAt, string title, int pages, ScanDestination destination) =>
        new(id, DarkroomKind.Scan, capturedAt)
        {
            Title = title,
            Pages = pages,
            Destination = destination,
        };

    public string Id { get; }

    public DarkroomKind Kind { get; }

    public DateTime CapturedAt { get; }

    /// <summary>The owner's voice, written from what the vision call saw. Empty until it lands.</summary>
    public string Caption { get; init; } = string.Empty;

    /// <summary>A scan's name. Empty on a photo.</summary>
    public string Title { get; init; } = string.Empty;

    public int Pages { get; init; }

    public ScanDestination Destination { get; init; }

    /// <summary>How many times the handoff has been tried. A scan only.</summary>
    public int Attempts { get; init; }
}

/// <summary>
/// The room everything waits in, and the phase's reversed spine.
///
/// <para>Gate 1 settled on "Plate never keeps anything — every capture is handed somewhere
/// and forgotten." The owner reversed it: <b>nothing leaves Plate until you have chosen it,
/// and nothing stays once you have.</b> Culling is a real job that nobody does inside Google
/// Photos, and someone who shoots a lot and never goes back is exactly who needs it.</para>
///
/// <para><b>What is deliberately not here.</b> No expiry, no auto-send, no nag. Four
/// mitigations for "Plate now holds the only copy" were offered and <i>nothing</i> was
/// chosen — "my darkroom, my job" — so their absence is the design, not an omission. A test
/// asserts that this type has no member named for any of them, so growing one has to be a
/// decision rather than a drift.</para>
///
/// <para>Immutable, like the scan tray and for the same reason: the head holds it in a field
/// and reassigns it, and an undo has to be able to put back exactly what was taken.</para>
/// </summary>
public sealed record Darkroom
{
    public static readonly Darkroom Empty = new([], []);

    private Darkroom(ImmutableList<DarkroomItem> items, ImmutableHashSet<string> chosen)
    {
        Items = items;
        this.chosen = chosen;
    }

    private readonly ImmutableHashSet<string> chosen;

    /// <summary>Newest first: a grid you have to scroll to reach what you just shot is a grid you stop using.</summary>
    public IReadOnlyList<DarkroomItem> Items { get; }

    public bool HasAnything => Items.Count > 0;

    public int ChosenCount => chosen.Count;

    public bool IsChosen(string id) => chosen.Contains(id);

    /// <summary>
    /// Puts something in the room. A capture that lands twice — a rebind or a rotation can
    /// replay the save callback — is the same picture, not two.
    /// </summary>
    public Darkroom Add(DarkroomItem item)
    {
        if (Items.Any(existing => existing.Id == item.Id))
        {
            return this;
        }

        return new Darkroom(
            [.. Items.Append(item).OrderByDescending(entry => entry.CapturedAt)], chosen);
    }

    /// <summary>Picks or unpicks one. An id that is no longer here changes nothing.</summary>
    public Darkroom Choose(string id)
    {
        if (!Items.Any(item => item.Id == id))
        {
            return this;
        }

        return new Darkroom(
            [.. Items], chosen.Contains(id) ? chosen.Remove(id) : chosen.Add(id));
    }

    /// <summary>
    /// Takes what was picked out of the room and hands it back, so the caller can send it or
    /// delete it — and, for a delete, put it back if the owner taps undo. With nothing
    /// picked this takes everything, because the bar says "Send all" in that state and it
    /// must do what it says.
    /// </summary>
    public (Darkroom Remaining, IReadOnlyList<DarkroomItem> Taken) TakeChosen()
    {
        var taken = chosen.Count == 0
            ? Items.ToList()
            : Items.Where(item => chosen.Contains(item.Id)).ToList();
        var remaining = Items.Where(item => !taken.Contains(item)).ToList();
        return (new Darkroom([.. remaining], []), taken);
    }

    /// <summary>
    /// Puts taken items back — the undo. They land where they were, because the room is
    /// ordered by when each thing was captured rather than by when it arrived here.
    /// </summary>
    public Darkroom Restore(IReadOnlyList<DarkroomItem> items) =>
        items.Aggregate(this, (room, item) => room.Add(item));

    /// <summary>
    /// Attaches the voice's line to one picture. The call is slow, so by the time it lands
    /// the owner may have deleted what it describes; that is not an error.
    /// </summary>
    public Darkroom WithCaption(string id, string caption) =>
        new([.. Items.Select(item => item.Id == id ? item with { Caption = caption } : item)], chosen);

    /// <summary>Records another try at a scan's handoff, for the row that shows it.</summary>
    public Darkroom WithAttempt(string id) =>
        new([.. Items.Select(item =>
            item.Id == id ? item with { Attempts = item.Attempts + 1 } : item)], chosen);
}
