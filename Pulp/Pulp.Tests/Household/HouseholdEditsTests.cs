using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Household;

/// <summary>
/// Every edit of a playlist or a child's pick is made against the household as it stands when the
/// edit lands, never against a copy the screen read when it opened, and every undo is the inverse
/// edit applied to the list as it is by then, at the place the entry stood. A snapshot written back
/// loses whatever happened in between — a tick made in the same picker session, a move made while
/// the dog-ear was up — and says nothing about it.
/// </summary>
public class HouseholdEditsTests
{
    private static readonly PlaylistId Rainy = new("playlist-rainy-example");
    private static readonly PlaylistId Weekend = new("playlist-weekend-example");

    private static readonly PlaylistEntry A = PlaylistEntry.ForTitle(Lantern, TitleKind.Film);
    private static readonly PlaylistEntry B = PlaylistEntry.ForTitle(Orchard, TitleKind.Show);
    private static readonly PlaylistEntry C = PlaylistEntry.ForTitle(Kestrel, TitleKind.Film);

    private static HouseholdDocument With(PlaylistId id, params PlaylistEntry[] entries)
    {
        var list = entries.Aggregate(Playlist.Named("Rainy Sundays"), (playlist, entry) => playlist.Add(entry));
        return HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Playlist, new HouseholdPlaylist(id, Robin, Shared: false, list), Next(), PhoneA);
    }

    private static HouseholdDocument Edit(HouseholdDocument document, PlaylistId id, Func<Playlist, Playlist> change) =>
        HouseholdEdits.EditPlaylist(document, id, change, Next(), PhoneA);

    private static IReadOnlyList<PlaylistEntry> Entries(HouseholdDocument document, PlaylistId id) =>
        HouseholdView.Of(document).Playlist(id)?.List.Entries ?? throw new InvalidOperationException($"No playlist {id} in the document.");

    private static IReadOnlyList<ParentPickEntry> Picks(HouseholdDocument document) =>
        HouseholdRecords.Read(document, HouseholdMaps.ParentPick).Items;

    // ---- B1: a child's pick ticked then unticked in one picker session ----

    [Fact]
    public void AChildsPickTickedThenUntickedInOneSessionIsRemoved()
    {
        // The picker opened on a household with no pick; the tick writes one.
        var opened = HouseholdDocument.Empty;
        var ticked = HouseholdRecords.Put(opened, HouseholdMaps.ParentPick, new ParentPickEntry(Pip, new ParentPick(Lantern, Robin, Evening)), Next(), PhoneA);

        // The untick removes by key from the household as it now stands.
        var unticked = new ParentPickRemoval(Pip, Lantern).Apply(ticked, Next(), PhoneA);

        Assert.Empty(Picks(unticked));
    }

    [Fact]
    public void UndoingAChildsPickRemovalPutsTheSamePickBack()
    {
        var picked = new ParentPick(Lantern, Avery, Evening);
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ParentPick, new ParentPickEntry(Pip, picked), Next(), PhoneA);
        var removal = new ParentPickRemoval(Pip, Lantern);

        var removed = removal.Apply(document, Next(), PhoneA);
        var undone = removal.Undo(removed, new ParentPick(Lantern, Robin, Evening.AddHours(1)), Next(), PhoneA);

        Assert.Equal(new ParentPickEntry(Pip, picked), Assert.Single(Picks(undone)));
    }

    [Fact]
    public void UndoingARemovalThatFoundNoPickPutsTheFallbackBack()
    {
        // The box was ticked when it was unticked, so its undo leaves the title on the child's Favorites.
        var removal = new ParentPickRemoval(Pip, Lantern);
        var fallback = new ParentPick(Lantern, Robin, Evening);

        var undone = removal.Undo(removal.Apply(HouseholdDocument.Empty, Next(), PhoneA), fallback, Next(), PhoneA);

        Assert.Equal(new ParentPickEntry(Pip, fallback), Assert.Single(Picks(undone)));
    }

    [Fact]
    public void RemovingAChildsPickLeavesAnotherChildsPickOfTheSameTitle()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ParentPick, new ParentPickEntry(Pip, new ParentPick(Lantern, Robin, Evening)), Next(), PhoneA);
        var other = new ViewerId("wren-example");
        document = HouseholdRecords.Put(document, HouseholdMaps.ParentPick, new ParentPickEntry(other, new ParentPick(Lantern, Robin, Evening)), Next(), PhoneA);

        var removed = new ParentPickRemoval(Pip, Lantern).Apply(document, Next(), PhoneA);

        Assert.Equal(other, Assert.Single(Picks(removed)).Child);
    }

    // ---- S1: a playlist ticked, unticked, then the untick undone ----

    [Fact]
    public void APlaylistTickUntickUndoLeavesTheTitleOnTheList()
    {
        var opened = With(Rainy, B);
        var ticked = Edit(opened, Rainy, list => new EntryAddition(A).Apply(list));
        var removal = new EntryRemoval(A);
        var unticked = Edit(ticked, Rainy, removal.Apply);

        var undone = Edit(unticked, Rainy, removal.Undo);

        Assert.Equal([B, A], Entries(undone, Rainy));
    }

    // ---- S2: undo puts the entry back where it stood, not at the end ----

    [Fact]
    public void UndoingARemovalPutsTheEntryBackWhereItStood()
    {
        var removal = new EntryRemoval(B);
        var list = Playlist.Named("Rainy Sundays").Add(A).Add(B).Add(C);

        var undone = removal.Undo(removal.Apply(list));

        Assert.Equal([A, B, C], undone.Entries);
    }

    [Fact]
    public void UndoingARemovalWhenTheListShrankPutsTheEntryAtTheEnd()
    {
        var removal = new EntryRemoval(C);
        var removed = removal.Apply(Playlist.Named("Rainy Sundays").Add(A).Add(B).Add(C));

        var undone = removal.Undo(removed.Remove(A));

        Assert.Equal([B, C], undone.Entries);
    }

    [Fact]
    public void UndoingARemovalOfAnEntryPutBackElsewhereChangesNothing()
    {
        var removal = new EntryRemoval(B);
        var removed = removal.Apply(Playlist.Named("Rainy Sundays").Add(A).Add(B).Add(C));
        var addedAgain = removed.Add(B);

        Assert.Equal([A, C, B], removal.Undo(addedAgain).Entries);
    }

    [Fact]
    public void UndoingAnAdditionOfAnEntryAlreadyThereLeavesIt()
    {
        var addition = new EntryAddition(A);
        var list = Playlist.Named("Rainy Sundays").Add(A).Add(B);

        Assert.Equal([A, B], addition.Undo(addition.Apply(list)).Entries);
    }

    [Fact]
    public void UndoingAnAdditionTakesOffWhatItAdded()
    {
        var addition = new EntryAddition(C);

        Assert.Equal([A, B], addition.Undo(addition.Apply(Playlist.Named("Rainy Sundays").Add(A).Add(B))).Entries);
    }

    // ---- S3: a move made while the dog-ear was up survives the undo ----

    [Fact]
    public void UndoingARemovalKeepsAMoveMadeWhileTheDogEarWasUp()
    {
        var removal = new EntryRemoval(A);
        var removed = Edit(With(Rainy, A, B, C), Rainy, removal.Apply);
        var moved = Edit(removed, Rainy, list => PlaylistEdits.Move(list, C, PlaylistMove.ToTop));

        var undone = Edit(moved, Rainy, removal.Undo);

        Assert.Equal([A, C, B], Entries(undone, Rainy));
    }

    [Fact]
    public void UndoingAMoveToAnotherPlaylistKeepsWhatTheTargetGainedSince()
    {
        var document = With(Rainy, A, B);
        document = HouseholdRecords.Put(document, HouseholdMaps.Playlist, new HouseholdPlaylist(Weekend, Robin, Shared: false, Playlist.Named("Weekend").Add(C)), Next(), PhoneA);
        var left = new EntryRemoval(A);
        var arrived = new EntryAddition(A);
        document = Edit(Edit(document, Weekend, arrived.Apply), Rainy, left.Apply);

        // Something else lands on the target before the undo.
        document = Edit(document, Weekend, list => list.Add(B));
        document = Edit(Edit(document, Weekend, arrived.Undo), Rainy, left.Undo);

        Assert.Equal([A, B], Entries(document, Rainy));
        Assert.Equal([C, B], Entries(document, Weekend));
    }

    [Fact]
    public void AMoveOfAnEntryNoLongerOnTheListChangesNothing()
    {
        var list = Playlist.Named("Rainy Sundays").Add(A).Add(B);

        Assert.Equal([A, B], PlaylistEdits.Move(list, C, PlaylistMove.ToTop).Entries);
    }

    [Fact]
    public void AMoveThatNoLongerChangesAnythingChangesNothing()
    {
        var list = Playlist.Named("Rainy Sundays").Add(A).Add(B);

        Assert.Equal([A, B], PlaylistEdits.Move(list, A, PlaylistMove.ToTop).Entries);
    }

    [Fact]
    public void EditingAPlaylistTheHouseholdNoLongerHoldsSaysWhich()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Edit(With(Rainy, A), Weekend, list => list.Add(B)));

        Assert.Contains(Weekend.Value, failure.Message, StringComparison.Ordinal);
    }

    // ---- one new-playlist id format ----

    [Fact]
    public void ANewPlaylistIdHasTheOneFormat()
    {
        var first = HouseholdEdits.NewPlaylistId();
        var second = HouseholdEdits.NewPlaylistId();

        Assert.StartsWith(HouseholdEdits.PlaylistIdPrefix, first.Value, StringComparison.Ordinal);
        Assert.Equal(HouseholdEdits.PlaylistIdPrefix.Length + 32, first.Value.Length);
        Assert.NotEqual(first, second);
    }
}
