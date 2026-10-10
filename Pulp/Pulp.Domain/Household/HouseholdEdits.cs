using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Domain.Household;

/// <summary>
/// Edits of a playlist or a child's pick, made against the household document as it stands when the
/// write lands (inside the household gate), never against a copy a screen read earlier. A screen says
/// <i>what</i> changes — this entry off that list, that title off this child's Favorites — and the
/// edit reads the current record and changes only that.
/// </summary>
public static class HouseholdEdits
{
    /// <summary>The one format a new playlist's id takes, whichever screen makes it.</summary>
    public const string PlaylistIdPrefix = "playlist-";

    public static PlaylistId NewPlaylistId() => new(PlaylistIdPrefix + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The document with <paramref name="change"/> applied to the playlist <paramref name="id"/> as the
    /// document holds it. A playlist the household no longer holds is not brought back by an edit of
    /// it; that throws, naming the playlist, so the write that tried is logged with what it was.
    /// </summary>
    public static HouseholdDocument EditPlaylist(
        HouseholdDocument document, PlaylistId id, Func<Playlist, Playlist> change, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(change);
        var current = HouseholdRecords.Read(document, HouseholdMaps.Playlist).Items.FirstOrDefault(playlist => playlist.Id == id)
            ?? throw new InvalidOperationException($"Playlist '{id}' is not in the household any more (removed elsewhere, or unreadable), so it was not edited.");
        var edited = change(current.List);
        return ReferenceEquals(edited, current.List)
            ? document
            : HouseholdRecords.Put(document, HouseholdMaps.Playlist, current with { List = edited }, stamp, writer);
    }

    /// <summary>The child's pick of a title as the document holds it, or null when there is none.</summary>
    public static ParentPick? ParentPickOf(HouseholdDocument document, ViewerId child, TitleId title) =>
        HouseholdRecords.Read(document, HouseholdMaps.ParentPick).Items
            .FirstOrDefault(entry => entry.Child == child && entry.Pick.Title == title)?.Pick;
}

/// <summary>
/// Taking a title off one child's Favorites, undo-first. A pick allow-lists its title for the child
/// (D255), so the removal goes by the record's key — the child and the title — against the household
/// as it stands, which also catches a pick ticked earlier in the same picker session. It remembers the
/// pick it took off so the undo puts that same pick back.
/// </summary>
public sealed class ParentPickRemoval(ViewerId child, TitleId title)
{
    private ParentPick? removed;

    public ViewerId Child { get; } = child;

    public TitleId Title { get; } = title;

    public HouseholdDocument Apply(HouseholdDocument document, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        removed = HouseholdEdits.ParentPickOf(document, Child, Title);

        // The key is the child and the title; the parent and the instant are not part of it.
        var key = new ParentPickEntry(Child, removed ?? new ParentPick(Title, Child, DateTimeOffset.UnixEpoch));
        return HouseholdRecords.Remove(document, HouseholdMaps.ParentPick, key, stamp, writer);
    }

    /// <summary>
    /// Puts the removed pick back; when the removal found none (the box was ticked, so its undo leaves
    /// the title on), <paramref name="fallback"/> is the pick written.
    /// </summary>
    public HouseholdDocument Undo(HouseholdDocument document, ParentPick fallback, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fallback);
        if (fallback.Title != Title)
        {
            throw new ArgumentException($"The fallback pick is for '{fallback.Title}', not '{Title}'.", nameof(fallback));
        }

        return HouseholdRecords.Put(document, HouseholdMaps.ParentPick, new ParentPickEntry(Child, removed ?? fallback), stamp, writer);
    }
}
