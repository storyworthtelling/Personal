using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>
/// The explicit cases behind G11. Every writer, key and value here is invented.
/// </summary>
public class HouseholdMergeTests
{
    private static readonly RecordKey Key = new("playlist", "sunday-night");

    private static HouseholdRecord Put(long wall, string writer, string name, int counter = 0) =>
        HouseholdRecord.Write(Key, 1, Fields(("name", name)), new HlcTimestamp(wall, counter), writer);

    private static HouseholdRecord Delete(long wall, string writer, int counter = 0) =>
        HouseholdRecord.Tombstone(Key, 1, new HlcTimestamp(wall, counter), writer);

    private static Dictionary<string, string> Fields(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    private static HouseholdDocument Doc(params HouseholdRecord[] records) =>
        records.Aggregate(HouseholdDocument.Empty, (document, record) => document.With(record));

    private static void AssertBothOrders(HouseholdDocument left, HouseholdDocument right, Action<HouseholdDocument> check)
    {
        var forwards = HouseholdMerge.Merge(left, right);
        var backwards = HouseholdMerge.Merge(right, left);
        check(forwards);
        check(backwards);
        Assert.True(forwards.ContentEquals(backwards), "merge gave a different document depending on which side read first");
    }

    [Fact]
    public void ADeleteIsNotResurrectedByAnOlderUpdate()
    {
        var olderUpdate = Doc(Put(500, "phone-b", "Old name"));
        var newerDelete = Doc(Delete(900, "phone-a"));

        AssertBothOrders(olderUpdate, newerDelete, merged =>
        {
            Assert.Null(merged.Live(Key));
            Assert.True(merged.Find(Key)!.IsTombstone);
        });
    }

    [Fact]
    public void ANewerUpdateWinsOverAnOlderDelete()
    {
        var olderDelete = Doc(Delete(500, "phone-a"));
        var newerUpdate = Doc(Put(900, "phone-b", "Brought back"));

        AssertBothOrders(olderDelete, newerUpdate, merged =>
            Assert.Equal("Brought back", merged.Live(Key)!.Fields["name"]));
    }

    [Fact]
    public void SameStampDifferentWritersBreaksTheTieTheSameWayEveryTime()
    {
        var fromA = Doc(Put(700, "phone-a", "From A"));
        var fromB = Doc(Put(700, "phone-b", "From B"));

        AssertBothOrders(fromA, fromB, merged =>
            Assert.Equal("From B", merged.Live(Key)!.Fields["name"]));
    }

    [Fact]
    public void SameStampSameWriterADeleteWins()
    {
        var update = Doc(Put(700, "phone-a", "Kept?"));
        var delete = Doc(Delete(700, "phone-a"));

        AssertBothOrders(update, delete, merged => Assert.Null(merged.Live(Key)));
    }

    [Fact]
    public void AWriterWhoseClockRunsBehindStillWinsOnceItHasReadTheFile()
    {
        // Phone B's clock is an hour slow. It reads the file (witnessing A's stamp) and then edits.
        var hour = 3600000L;
        var clockA = new HybridLogicalClock();
        var clockB = new HybridLogicalClock();
        var fromA = Doc(HouseholdRecord.Write(Key, 1, Fields(("name", "A's edit")), clockA.Now(10 * hour), "phone-a"));

        clockB.Witness(fromA.MaxStamp, 9 * hour);
        var fromB = fromA.With(fromA.Find(Key)!.Edit(Fields(("name", "B's later edit")), 1, clockB.Now(9 * hour + 1000), "phone-b"));

        AssertBothOrders(fromA, fromB, merged =>
            Assert.Equal("B's later edit", merged.Live(Key)!.Fields["name"]));
    }

    [Fact]
    public void AWriterWhoseClockRunsBehindAndNeverReadTheFileLosesAndThatIsTheAcceptedCost()
    {
        // Without reading first there is nothing to witness: physical time decides, and the slow
        // clock's edit loses even though it happened later in real time. The publisher always reads
        // before it writes (HouseholdPublish), which is what keeps this case off the real path.
        var hour = 3600000L;
        var fromA = Doc(Put(10 * hour, "phone-a", "A's edit"));
        var fromB = Doc(Put(9 * hour + 1000, "phone-b", "B's blind edit"));

        AssertBothOrders(fromA, fromB, merged =>
            Assert.Equal("A's edit", merged.Live(Key)!.Fields["name"]));
    }

    [Fact]
    public void MergingADocumentWithItselfChangesNothing()
    {
        var document = Doc(Put(1, "phone-a", "One"), Delete(2, "phone-b"), Put(3, "phone-a", "Three"));
        var other = Doc(HouseholdRecord.Write(new RecordKey("watched", "film-9"), 1, Fields(("viewer", "ada")), new HlcTimestamp(4, 0), "phone-b"));
        var both = HouseholdMerge.Merge(document, other);

        Assert.True(HouseholdMerge.Merge(both, both).ContentEquals(both));
        Assert.Equal(2, both.Records.Count);
    }

    [Fact]
    public void RecordsUnderDifferentKeysAreAllKept()
    {
        var left = Doc(HouseholdRecord.Write(new RecordKey("watched", "film-1"), 1, Fields(), new HlcTimestamp(1, 0), "phone-a"));
        var right = Doc(HouseholdRecord.Write(new RecordKey("watched", "film-2"), 1, Fields(), new HlcTimestamp(1, 0), "phone-b"));

        var merged = HouseholdMerge.Merge(left, right);

        Assert.NotNull(merged.Live(new RecordKey("watched", "film-1")));
        Assert.NotNull(merged.Live(new RecordKey("watched", "film-2")));
    }

    [Fact]
    public void KeysCompareWithoutCaseLikeTheIdsTheyCarry()
    {
        Assert.Equal(new RecordKey("Watched", "Film-1"), new RecordKey("watched", "film-1"));
        Assert.Equal("watched/film-1", new RecordKey("Watched", "Film-1").Value);
    }

    [Fact]
    public void AKeyNeedsAKindAndAnIdAndTheKindCannotHoldTheSeparator()
    {
        Assert.Throws<ArgumentException>(() => new RecordKey("", "x"));
        Assert.Throws<ArgumentException>(() => new RecordKey("watched", " "));
        Assert.Throws<ArgumentException>(() => new RecordKey("a/b", "x"));
    }

    [Fact]
    public void ARecordNeedsAWriterAndAPositiveSchemaVersion()
    {
        Assert.Throws<ArgumentException>(() => HouseholdRecord.Write(Key, 1, Fields(), new HlcTimestamp(1, 0), " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => HouseholdRecord.Write(Key, 0, Fields(), new HlcTimestamp(1, 0), "phone-a"));
    }

    [Fact]
    public void ATombstoneCarriesNoFields()
    {
        var document = Doc(Put(1, "phone-a", "Gone soon"));

        var deleted = document.With(document.Find(Key)!.Delete(new HlcTimestamp(2, 0), "phone-b"));

        Assert.Empty(deleted.Find(Key)!.Fields);
        Assert.True(deleted.Find(Key)!.IsTombstone);
    }

    [Fact]
    public void ARecordIsImmuneToTheCallerChangingTheDictionaryItWasBuiltFrom()
    {
        var fields = Fields(("name", "Original"));
        var record = HouseholdRecord.Write(Key, 1, fields, new HlcTimestamp(1, 0), "phone-a");

        fields["name"] = "Changed behind its back";

        Assert.Equal("Original", record.Fields["name"]);
    }
}
