using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Domain.Household.Records;

/// <summary>
/// One viewer's one sitting. <see cref="WatchEvent"/> carries no viewer because the Library's lists
/// are always one viewer's (D97); the household file holds everyone's, so the record names whose it
/// is. Private viewing is a display rule over a file both adults can decrypt (finding §2.7).
/// </summary>
public sealed record WatchedSitting(ViewerId Viewer, WatchEvent Sitting);

/// <summary>
/// A playlist's identity. <see cref="Library.Playlist"/> is only a name and an order, and the
/// calendar's <see cref="PlaylistShelf"/> only a name, an owner and a flag — neither can tell two
/// lists called "Weekend" apart. The id is the record's key, so there is one playlist identity and
/// the shelf is read off it (<see cref="HouseholdPlaylist.Shelf"/>) rather than kept as a second copy.
/// </summary>
public readonly record struct PlaylistId
{
    public PlaylistId(string value) =>
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A playlist id needs a value; an empty one would collide every playlist.", nameof(value))
            : value;

    public string Value { get; }

    public bool Equals(PlaylistId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>
/// A playlist as the household keeps it: its id, whose it is, whether both adults see it (D97,
/// D244), and the hand-ordered list itself (D103). The whole list is one record, so two writers
/// reordering one playlist at once resolve last-writer-wins rather than interleaving their drags.
/// </summary>
public sealed record HouseholdPlaylist(PlaylistId Id, ViewerId Owner, bool Shared, Playlist List)
{
    /// <summary>The calendar's view of this playlist (D244), derived — never stored on its own.</summary>
    public PlaylistShelf Shelf => new(List.Name, Owner, Shared);

    /// <summary>Own playlists and shared ones; never another viewer's private list (D97).</summary>
    public bool IsVisibleTo(ViewerId viewer) => Shared || Owner == viewer;
}

/// <summary>"Open there instead" (D216): this title's home from now on, for the household.</summary>
public sealed record HomeChoice(TitleId Title, ServiceId Service);

internal sealed class WatchedMap : RecordMap<WatchedSitting>
{
    private static readonly FieldWords<WatchedKind> Kinds = new(new Dictionary<WatchedKind, string>
    {
        [WatchedKind.Film] = "film",
        [WatchedKind.Show] = "show",
        [WatchedKind.Game] = "game",
    });

    private static readonly FieldWords<WatchSource> Sources = new(new Dictionary<WatchSource, string>
    {
        [WatchSource.Session] = "session",
        [WatchSource.HandOff] = "hand-off",
        [WatchSource.MarkedWatched] = "marked-watched",
        [WatchSource.MarkedUnwatched] = "marked-unwatched",
    });

    public override string Kind => HouseholdKinds.Watched;

    /// <summary>Viewer, title, the instant and the episode: one sitting is one record.</summary>
    protected override string IdOf(WatchedSitting value) => RecordIds.Join(
        value.Viewer.Value,
        value.Sitting.Title.Value,
        value.Sitting.At.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
        value.Sitting.Episode is { } episode ? $"s{episode.Season}e{episode.Number}" : "whole");

    public override IReadOnlyDictionary<string, string> FieldsOf(WatchedSitting value)
    {
        var sitting = value.Sitting;
        return new FieldWriter()
            .Text("viewer", value.Viewer.Value)
            .Text("title", sitting.Title.Value)
            .Word("kind", Kinds, sitting.Kind)
            .Instant("at", sitting.At)
            .Text("service", sitting.Service?.Value)
            .Number("season", sitting.Episode?.Season)
            .Number("episode", sitting.Episode?.Number)
            .Word("evidence", Sources, sitting.Evidence)
            .Length("stoppedMs", sitting.StoppedAt)
            .Length("runtimeMs", sitting.Runtime)
            .Flag("ended", sitting.Ended)
            .Fields;
    }

    public override WatchedSitting Read(RecordReader record)
    {
        var season = record.OptionalInt("season");
        var number = record.OptionalInt("episode");
        if ((season is null) != (number is null))
        {
            throw record.Fail("episode", "has a season without a number, or a number without a season");
        }

        var service = record.OptionalText("service");
        return new WatchedSitting(
            new ViewerId(record.Text("viewer")),
            new WatchEvent(
                new TitleId(record.Text("title")),
                record.Word("kind", Kinds),
                record.Instant("at"),
                service is null ? null : new ServiceId(service),
                season is null ? null : new EpisodeRef(season.Value, number!.Value),
                record.Word("evidence", Sources),
                record.OptionalLength("stoppedMs"),
                record.OptionalLength("runtimeMs"),
                record.OptionalText("ended") is not null && record.Flag("ended")));
    }
}

internal sealed class PlaylistMap : RecordMap<HouseholdPlaylist>
{
    private static readonly FieldWords<PlaylistEntryKind> EntryKinds = new(new Dictionary<PlaylistEntryKind, string>
    {
        [PlaylistEntryKind.Film] = "film",
        [PlaylistEntryKind.Show] = "show",
        [PlaylistEntryKind.Game] = "game",
        [PlaylistEntryKind.Franchise] = "franchise",
    });

    public override string Kind => HouseholdKinds.Playlist;

    protected override string IdOf(HouseholdPlaylist value) => value.Id.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(HouseholdPlaylist value) =>
        new FieldWriter()
            .Text("id", value.Id.Value)
            .Text("name", value.List.Name)
            .Text("owner", value.Owner.Value)
            .Flag("shared", value.Shared)
            // Each entry is "word:key" with the key escaped; the list escapes it again as an item.
            .List("entries", value.List.Entries.Select(entry => EntryKinds.Word(entry.Kind) + ":" + Uri.EscapeDataString(entry.Key)))
            .Fields;

    public override HouseholdPlaylist Read(RecordReader record)
    {
        var list = Playlist.Named(record.Text("name"));
        foreach (var item in record.List("entries"))
        {
            var split = item.IndexOf(':', StringComparison.Ordinal);
            if (split <= 0 || split == item.Length - 1 || !EntryKinds.TryValue(item[..split], out var kind))
            {
                throw record.Fail("entries", $"holds '{item}', not a known entry");
            }

            list = list.Add(new PlaylistEntry(kind, Uri.UnescapeDataString(item[(split + 1)..])));
        }

        return new HouseholdPlaylist(
            new PlaylistId(record.OptionalText("id") ?? record.Key.Id),
            new ViewerId(record.Text("owner")),
            record.Flag("shared"),
            list);
    }
}

internal sealed class HomeOverrideMap : RecordMap<HomeChoice>
{
    public override string Kind => HouseholdKinds.HomeOverride;

    protected override string IdOf(HomeChoice value) => value.Title.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(HomeChoice value) =>
        new FieldWriter().Text("title", value.Title.Value).Text("service", value.Service.Value).Fields;

    public override HomeChoice Read(RecordReader record) =>
        new(new TitleId(record.Text("title")), new ServiceId(record.Text("service")));
}
