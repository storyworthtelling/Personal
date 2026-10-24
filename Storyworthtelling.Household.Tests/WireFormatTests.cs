using System.Text;
using Storyworthtelling.Household;

namespace Storyworthtelling.Household.Tests;

/// <summary>
/// The library must read and write exactly the bytes Pulp's household file holds, or a phone on one
/// engine and a phone on the other would rewrite each other's file forever. The sample was produced
/// by Pulp's own writer from invented data and is checked in; nothing here references Pulp.
/// </summary>
public class WireFormatTests
{
    private static string Sample() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "household-written-by-pulp.json"), Encoding.UTF8);

    [Fact]
    public void APulpFileReWritesByteForByte()
    {
        var text = Sample();

        var again = HouseholdDocumentCodec.Serialize(HouseholdDocumentCodec.Deserialize(text));

        Assert.Equal(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetBytes(again));
    }

    [Fact]
    public void APulpFileHoldsWhatItSays()
    {
        var document = HouseholdDocumentCodec.Deserialize(Sample());

        Assert.Equal(4, document.Records.Count);
        var playlist = document.Find(new RecordKey("playlist", "pl-1-example"))!;
        Assert.Equal(3, playlist.SchemaVersion);
        Assert.Equal(new HlcTimestamp(1_760_000_000_123, 4), playlist.Stamp);
        Assert.Equal("Rainy \"Sundays\" — ünïcode <tag> & more", playlist.Fields["name"]);
        Assert.Equal("", playlist.Fields["empty"]);
        Assert.True(document.Find(new RecordKey("veto", "robin:kestrel"))!.IsTombstone);
    }

    [Fact]
    public void AKindNobodyRegisteredIsKeptAndMerged()
    {
        var document = HouseholdDocumentCodec.Deserialize(Sample());
        var meals = new RecordKey("meal-plan", "week-42");
        var newer = document.Find(meals)!.Edit(
            new Dictionary<string, string> { ["days"] = "6" },
            1,
            new HlcTimestamp(1_760_000_002_000, 0),
            "install-a-example");

        var merged = HouseholdMerge.Merge(document, HouseholdDocument.Empty.With(newer));
        var back = HouseholdDocumentCodec.Deserialize(HouseholdDocumentCodec.Serialize(merged));

        Assert.Equal("6", back.Live(meals)!.Fields["days"]);
        Assert.Equal("Sam cooks; Lee shops", back.Live(meals)!.Fields["note"]);
        Assert.Equal(7, back.Live(meals)!.SchemaVersion);
        Assert.Equal(4, back.Records.Count);
    }

    [Fact]
    public void TheWireNamesAreTheOnesPulpWrites()
    {
        var text = Sample();

        foreach (var name in new[] { "\"format\"", "\"records\"", "\"kind\"", "\"id\"", "\"schema\"", "\"wall\"", "\"counter\"", "\"writer\"", "\"deleted\"", "\"fields\"" })
        {
            Assert.Contains(name, text, StringComparison.Ordinal);
        }

        Assert.Equal(1, HouseholdDocument.FormatVersion);
    }
}
