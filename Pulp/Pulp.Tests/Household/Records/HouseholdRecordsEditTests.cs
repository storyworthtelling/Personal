using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Household.Records;

/// <summary>
/// Writing a typed value over a record that is already there. The schema-version rule (plan choice
/// P3) is that a writer changes what it knows and carries the rest: the TV phase adds fields to
/// records rather than migrating, which holds only if a phone that rewrites a playlist from its own
/// typed model keeps the TV's fields instead of rebuilding the record from what it knows.
/// </summary>
public class HouseholdRecordsEditTests
{
    private static HouseholdPlaylist Rainy(bool shared) => new(
        new PlaylistId("pl-rainy-example"),
        Robin,
        shared,
        Playlist.Named("Rainy Sundays").Add(new PlaylistEntry(PlaylistEntryKind.Film, Lantern.Value)));

    [Fact]
    public void FieldsFromANewerWriterSurviveAnOlderWritersEdit()
    {
        var phone = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Playlist, Rainy(shared: false), Next(), PhoneA);
        var original = Assert.Single(phone.Records);

        // The TV, on schema 2, adds a field the phone's schema has never heard of.
        var fromTv = original.Edit(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["screen"] = "den" }, 2, Next(), Tv);
        var document = phone.With(fromTv);

        // The phone shares the playlist, rewriting the record from its typed model.
        var edited = HouseholdRecords.Put(document, HouseholdMaps.Playlist, Rainy(shared: true), Next(), PhoneA);

        var record = Assert.Single(edited.Records);
        Assert.Equal("den", record.Fields["screen"]);
        Assert.Equal(2, record.SchemaVersion);
        Assert.True(Assert.Single(HouseholdRecords.Read(edited, HouseholdMaps.Playlist).Items).Shared);
    }

    [Fact]
    public void AnOptionalFieldClearedOnEditReadsBackAsAbsent()
    {
        var withService = new WatchedSitting(Robin, new WatchEvent(Lantern, WatchedKind.Film, Evening, Streamly));
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Watched, withService, Next(), PhoneA);

        // Same sitting (same key), the service no longer known: the edit must not carry the old one.
        var without = withService with { Sitting = withService.Sitting with { Service = null } };
        document = HouseholdRecords.Put(document, HouseholdMaps.Watched, without, Next(), PhoneA);

        Assert.Null(Assert.Single(HouseholdRecords.Read(document, HouseholdMaps.Watched).Items).Sitting.Service);
    }

    [Fact]
    public void RemovingWritesATombstoneThatOutranksTheOlderCopy()
    {
        var veto = new ViewerVeto(Robin, new Domain.Friday.Veto(Kestrel, ["heist"]));
        var phoneA = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Veto, veto, Next(), PhoneA);
        var phoneB = phoneA;

        // "Bring it back" on phone A; phone B still holds the veto.
        phoneA = HouseholdRecords.Remove(phoneA, HouseholdMaps.Veto, veto, Next(), PhoneA);

        var merged = HouseholdMerge.Merge(phoneB, phoneA);
        Assert.Empty(HouseholdRecords.Read(merged, HouseholdMaps.Veto).Items);
        Assert.True(Assert.Single(merged.Records).IsTombstone);
    }

    [Fact]
    public void PuttingAfterARemoveBringsTheRecordBack()
    {
        var viewer = new HouseholdViewer(Pip, IsChild: true);
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Viewer, viewer, Next(), PhoneA);
        document = HouseholdRecords.Remove(document, HouseholdMaps.Viewer, viewer, Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.Viewer, viewer, Next(), PhoneB);

        Assert.Equal(viewer, Assert.Single(HouseholdRecords.Read(document, HouseholdMaps.Viewer).Items));
    }

    [Fact]
    public void AStaleStampIsRefusedRatherThanLostInTheMerge()
    {
        var viewer = new HouseholdViewer(Pip, IsChild: true);
        var stamp = Next();
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Viewer, viewer, stamp, PhoneA);

        Assert.Throws<ArgumentException>(() =>
            HouseholdRecords.Put(document, HouseholdMaps.Viewer, viewer with { IsChild = false }, stamp, PhoneB));
    }

    [Fact]
    public void ARecordANewerWriterMadeUnreadableIsReportedNotDropped()
    {
        var good = new WatchedSitting(Robin, new WatchEvent(Lantern, WatchedKind.Film, Evening, Streamly));
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Watched, good, Next(), PhoneA);

        // A newer writer's evidence word this phone does not know.
        var strange = HouseholdRecord.Write(
            new RecordKey(HouseholdKinds.Watched, "from-the-future"),
            2,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["viewer"] = Robin.Value,
                ["title"] = Orchard.Value,
                ["kind"] = "show",
                ["at"] = Evening.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["evidence"] = "watched-together-on-tv",
            },
            Next(),
            Tv);
        document = document.With(strange);

        var read = HouseholdRecords.Read(document, HouseholdMaps.Watched);

        Assert.Equal(good, Assert.Single(read.Items));
        var unreadable = Assert.Single(read.Unreadable);
        Assert.Equal(strange.Key, unreadable.Key);
        Assert.Contains("evidence", unreadable.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingOneKindIgnoresTheOthers()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Viewer, new HouseholdViewer(Robin, false), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.HouseholdService, new HouseholdServiceEntry(Streamly), Next(), PhoneA);

        Assert.Single(HouseholdRecords.Read(document, HouseholdMaps.Viewer).Items);
        Assert.Empty(HouseholdRecords.Read(document, HouseholdMaps.Viewer).Unreadable);
        Assert.Single(HouseholdRecords.Read(document, HouseholdMaps.HouseholdService).Items);
    }

    [Fact]
    public void TwoSittingsOfOneTitleAreTwoRecords()
    {
        var first = new WatchedSitting(Robin, new WatchEvent(Orchard, WatchedKind.Show, Evening, Streamly, new EpisodeRef(1, 1)));
        var second = first with { Sitting = first.Sitting with { Episode = new EpisodeRef(1, 2), At = Evening.AddHours(1) } };
        var someoneElse = first with { Viewer = Avery };

        var document = HouseholdDocument.Empty;
        foreach (var sitting in new[] { first, second, someoneElse })
        {
            document = HouseholdRecords.Put(document, HouseholdMaps.Watched, sitting, Next(), PhoneA);
        }

        Assert.Equal(3, HouseholdRecords.Read(document, HouseholdMaps.Watched).Items.Count);
    }

    [Fact]
    public void AnIdWithTheKeySeparatorsInItCannotCollideWithAnother()
    {
        var odd = new ParentPickEntry(new ViewerId("a:b"), new Domain.Library.ParentPick(new TitleId("c"), Robin, Evening));
        var other = new ParentPickEntry(new ViewerId("a"), new Domain.Library.ParentPick(new TitleId("b:c"), Robin, Evening));

        Assert.NotEqual(HouseholdMaps.ParentPick.KeyOf(odd), HouseholdMaps.ParentPick.KeyOf(other));
    }
}
