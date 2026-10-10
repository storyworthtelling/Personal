using Pulp.Domain.Children;
using Pulp.Domain.Library;

namespace Pulp.Domain.Actions;

/// <summary>
/// One of the viewer's playlists as the picker sees it: its id, the list itself, whether it is shared
/// (otherwise it is the viewer's own), and when the viewer last added to it — null if never.
/// </summary>
public sealed record PickerPlaylist(string Id, Playlist Playlist, bool Shared, DateTimeOffset? LastUsed);

/// <summary>A child whose Favorites is a destination (D254), with the name the row shows.</summary>
public sealed record PickerChild(string Name, ChildRules Rules);

/// <summary>The picker's two groups (D254).</summary>
public enum PickerGroup
{
    Playlists,
    Favorites,
}

/// <summary>The one neutral badge a child's row may carry (D258).</summary>
public enum ChildBadge
{
    None,

    /// <summary>Over the child's ceiling (or unrated): ticking it allow-lists it for them (D255).</summary>
    AboveBand,

    /// <summary>On the child's block-list: greyed, cannot be ticked (D255, D258).</summary>
    Blocked,
}

/// <summary>What a tap on a row asks the caller to do.</summary>
public enum PickerChange
{
    /// <summary>Add at once; the filled checkbox is the success state, with no toast (D257).</summary>
    Add,

    /// <summary>Add as a parent's pick, which also allow-lists the title for that child (D255).</summary>
    AddAndAllowList,

    /// <summary>Remove at once and fold the undo dog-ear (D257, §13).</summary>
    RemoveUndoFirst,

    /// <summary>Nothing happens: the row is greyed.</summary>
    Refused,
}

/// <summary>
/// One row. <see cref="Key"/> is the playlist id or the child's viewer id; <see cref="Tag"/> is a
/// playlist's "shared" or "yours" (null on a child's row, which carries <see cref="Badge"/> instead).
/// </summary>
public sealed record PickerRow(
    PickerGroup Group, string Key, string Label, string? Tag, bool Ticked, bool Enabled, ChildBadge Badge);

public sealed record PickerModel(string Heading, IReadOnlyList<PickerRow> Playlists, IReadOnlyList<PickerRow> Favorites);

/// <summary>
/// Add to… (mockup 11, D259): two groups — the viewer's playlists, last-used first (D109), and each
/// child's Favorites (D254) — each row a checkbox, so a title goes onto several at once (D257). A tick
/// takes effect as it is made; an untick is undo-first. Model-made playlists start from Playlists, not
/// here (D104), so this only lists and ticks.
/// </summary>
public static class AddToPickerPlan
{
    public const string PlaylistsLabel = "Playlists";
    public const string FavoritesLabel = "Favorites";
    public const string NewPlaylistLabel = "New playlist…";
    public const string SharedTag = "shared";
    public const string YoursTag = "yours";
    public const string AboveBandBadge = "Above band";
    public const string BlockedBadge = "Blocked";

    /// <param name="title">
    /// The title's facts, which place it for each child. Null only for an entry no child's Favorites
    /// can hold — a game or a franchise — and then the Favorites group is empty: a child's sport is a
    /// parent's team allow-list (D59), not a favourite.
    /// </param>
    public static PickerModel Build(
        string titleName,
        PlaylistEntry entry,
        TitleFacts? title,
        IEnumerable<PickerPlaylist> playlists,
        IEnumerable<PickerChild> children)
    {
        if (string.IsNullOrWhiteSpace(titleName))
        {
            throw new ArgumentException("The picker is headed by the title being added.", nameof(titleName));
        }

        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(playlists);
        ArgumentNullException.ThrowIfNull(children);

        var isTitle = entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show;
        if (title is not null && (!isTitle || !string.Equals(title.Id.Value, entry.Key, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"The facts are for '{title.Id}', but the entry being added is '{entry.Key}'.", nameof(title));
        }

        var playlistRows = playlists
            .OrderBy(choice => choice.LastUsed is null)
            .ThenByDescending(choice => choice.LastUsed)
            .ThenBy(choice => choice.Playlist.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(choice => new PickerRow(
                PickerGroup.Playlists,
                choice.Id,
                choice.Playlist.Name,
                choice.Shared ? SharedTag : YoursTag,
                Ticked: choice.Playlist.Entries.Contains(entry),
                Enabled: true,
                ChildBadge.None))
            .ToList();

        var childList = children.ToList();
        if (!isTitle)
        {
            return new PickerModel(Heading(titleName), playlistRows, []);
        }

        if (title is null && childList.Count > 0)
        {
            throw new ArgumentException(
                "A film or a show needs its facts to say how it sits with each child's ceiling.", nameof(title));
        }

        var favoriteRows = childList.Select(child => ChildRow(child, title!)).ToList();
        return new PickerModel(Heading(titleName), playlistRows, favoriteRows);
    }

    public static PickerChange Tap(PickerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.Enabled)
        {
            return PickerChange.Refused;
        }

        if (row.Ticked)
        {
            return PickerChange.RemoveUndoFirst;
        }

        return row.Badge == ChildBadge.AboveBand ? PickerChange.AddAndAllowList : PickerChange.Add;
    }

    public static string? BadgeLabel(ChildBadge badge) => badge switch
    {
        ChildBadge.None => null,
        ChildBadge.AboveBand => AboveBandBadge,
        ChildBadge.Blocked => BlockedBadge,
        _ => throw new ArgumentOutOfRangeException(nameof(badge), badge, "A picker badge with no label."),
    };

    private static string Heading(string titleName) => $"Add {titleName.Trim()} to";

    private static PickerRow ChildRow(PickerChild child, TitleFacts title)
    {
        ArgumentNullException.ThrowIfNull(child);
        var rules = child.Rules;
        var ticked = rules.ParentFavorites.Contains(title.Id);

        // The badge says where the title would sit WITHOUT this pick: a pick allow-lists its title
        // (D255), so asking the filter with it in place would always answer "shown" and the badge
        // would vanish the moment the box was ticked.
        var withoutPick = rules with { ParentPicks = rules.ParentPicks.Where(pick => pick.Title != title.Id).ToList() };
        var badge = ChildFilter.Decide(withoutPick, title).Reason switch
        {
            FilterReason.Blocked => ChildBadge.Blocked,
            FilterReason.AboveCeiling or FilterReason.Unrated => ChildBadge.AboveBand,
            _ => ChildBadge.None,
        };

        return new PickerRow(
            PickerGroup.Favorites,
            rules.Child.Value,
            child.Name,
            Tag: null,
            Ticked: ticked,
            Enabled: badge != ChildBadge.Blocked,
            badge);
    }
}
