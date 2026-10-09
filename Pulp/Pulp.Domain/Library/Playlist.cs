using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>What a playlist can hold (D100): films, whole shows, games and franchises — never a season or an episode.</summary>
public enum PlaylistEntryKind
{
    Film,
    Show,
    Game,
    Franchise,
}

/// <summary>One thing on a playlist, by kind and the id its own source gives it, compared without case.</summary>
public sealed record PlaylistEntry
{
    public PlaylistEntry(PlaylistEntryKind kind, string key)
    {
        Kind = kind;
        Key = string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException("A playlist entry needs an id; an empty one would match nothing.", nameof(key))
            : key;
    }

    public PlaylistEntryKind Kind { get; }

    public string Key { get; }

    public static PlaylistEntry ForTitle(TitleId title, TitleKind kind) =>
        new(kind == TitleKind.Film ? PlaylistEntryKind.Film : PlaylistEntryKind.Show, title.Value);

    public bool Equals(PlaylistEntry? other) =>
        other is not null && Kind == other.Kind && StringComparer.OrdinalIgnoreCase.Equals(Key, other.Key);

    public override int GetHashCode() => HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Key));
}

/// <summary>A named, hand-ordered list (D103). Immutable: every edit returns the new list.</summary>
public sealed class Playlist
{
    private Playlist(string name, IReadOnlyList<PlaylistEntry> entries)
    {
        Name = name;
        Entries = entries;
    }

    public string Name { get; }

    public IReadOnlyList<PlaylistEntry> Entries { get; }

    public static Playlist Named(string name) =>
        new(string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A playlist needs a name.", nameof(name)) : name, []);

    /// <summary>New entries go to the bottom (D103); one already on the list stays where the owner put it.</summary>
    public Playlist Add(PlaylistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Entries.Contains(entry) ? this : new Playlist(Name, [.. Entries, entry]);
    }

    /// <summary>The drag (D103): the entry lands at <paramref name="toIndex"/> and everything else keeps its order.</summary>
    public Playlist Move(PlaylistEntry entry, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var from = IndexOf(entry);
        if (from < 0)
        {
            throw new ArgumentException("Only an entry on this playlist can be moved.", nameof(entry));
        }

        if (toIndex < 0 || toIndex >= Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(toIndex), toIndex, "A playlist entry can move only to a place on the list.");
        }

        var reordered = Entries.ToList();
        reordered.RemoveAt(from);
        reordered.Insert(toIndex, entry);
        return new Playlist(Name, reordered);
    }

    /// <summary>Removing is the only way an entry leaves; watching one strikes it (D101), it does not remove it.</summary>
    public Playlist Remove(PlaylistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new Playlist(Name, Entries.Where(e => e != entry).ToList());
    }

    private int IndexOf(PlaylistEntry entry)
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i] == entry)
            {
                return i;
            }
        }

        return -1;
    }
}
