using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>
/// Plan choice P3: the TV phase adds fields to records rather than migrating them. That holds only
/// if a writer that does not know a field leaves it alone — a phone on schema 1 editing a record the
/// TV wrote at schema 2 must not strip the TV's fields by rebuilding the record from what it knows.
/// </summary>
public class HouseholdSchemaTests
{
    private static readonly RecordKey Key = new("watched", "show-17");

    private static Dictionary<string, string> Fields(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    [Fact]
    public void AnOlderWriterKeepsTheFieldsItDoesNotKnow()
    {
        var fromTv = HouseholdRecord.Write(Key, 2, Fields(("episode", "4"), ("screen", "den")), new HlcTimestamp(100, 0), "tv-c");

        var fromPhone = fromTv.Edit(Fields(("episode", "5")), 1, new HlcTimestamp(200, 0), "phone-a");

        Assert.Equal("5", fromPhone.Fields["episode"]);
        Assert.Equal("den", fromPhone.Fields["screen"]);
        Assert.Equal(2, fromPhone.SchemaVersion);
    }

    [Fact]
    public void ANewerWriterRaisesTheVersion()
    {
        var fromPhone = HouseholdRecord.Write(Key, 1, Fields(("episode", "4")), new HlcTimestamp(100, 0), "phone-a");

        var fromTv = fromPhone.Edit(Fields(("screen", "den")), 2, new HlcTimestamp(200, 0), "tv-c");

        Assert.Equal(2, fromTv.SchemaVersion);
        Assert.Equal("4", fromTv.Fields["episode"]);
    }

    [Fact]
    public void ADeleteKeepsTheVersionSoAnOlderWriterCannotQuietlyDowngradeIt()
    {
        var fromTv = HouseholdRecord.Write(Key, 2, Fields(("screen", "den")), new HlcTimestamp(100, 0), "tv-c");

        var deleted = fromTv.Delete(new HlcTimestamp(200, 0), "phone-a");

        Assert.Equal(2, deleted.SchemaVersion);
    }

    [Fact]
    public void EditingATombstoneStartsFromNothingRatherThanReviving()
    {
        var gone = HouseholdRecord.Tombstone(Key, 1, new HlcTimestamp(100, 0), "phone-a");

        var back = gone.Edit(Fields(("episode", "1")), 1, new HlcTimestamp(200, 0), "phone-b");

        Assert.False(back.IsTombstone);
        Assert.Single(back.Fields);
    }

    [Fact]
    public void AnEditMustBeNewerThanTheRecordItEdits()
    {
        var record = HouseholdRecord.Write(Key, 1, Fields(("episode", "1")), new HlcTimestamp(500, 0), "phone-a");

        // An edit stamped before what it edits would lose to its own starting point and vanish.
        Assert.Throws<ArgumentException>(() => record.Edit(Fields(("episode", "2")), 1, new HlcTimestamp(400, 0), "phone-b"));
        Assert.Throws<ArgumentException>(() => record.Delete(new HlcTimestamp(500, 0), "phone-b"));
    }

    [Fact]
    public void TheFormatVersionIsStated() => Assert.True(HouseholdDocument.FormatVersion >= 1);
}
