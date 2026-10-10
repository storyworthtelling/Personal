using System.Text.Json.Nodes;
using Pulp.Domain.Household;
using Pulp.Infrastructure.Household;

namespace Pulp.Tests.Infrastructure.Household;

/// <summary>
/// The household document ⇄ JSON. Everything Domain's merge relies on — key, stamp, writer, schema,
/// tombstone, every field including ones this build has never heard of — has to come back exactly,
/// or two phones that read the same file would hold two different households and never converge.
/// </summary>
public class HouseholdDocumentCodecTests
{
    private static Dictionary<string, string> Fields(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    private static HouseholdDocument Sample() =>
        HouseholdDocument.Empty
            .With(HouseholdRecord.Write(
                new RecordKey("playlist", "pl-1-example"),
                3,
                Fields(("name", "Rainy \"Sundays\" — ünïcode"), ("screen", "den"), ("empty", "")),
                new HlcTimestamp(1_760_000_000_123, 4),
                "install-a-example"))
            .With(HouseholdRecord.Tombstone(new RecordKey("veto", "robin:kestrel"), 2, new HlcTimestamp(1_760_000_000_500, 0), "install-b-example"))
            .With(HouseholdRecord.Write(new RecordKey("viewer", "pip-example"), 1, Fields(("child", "true")), new HlcTimestamp(5, 0), "install-a-example"));

    [Fact]
    public void ADocumentRoundTripsExactly()
    {
        var document = Sample();

        var back = HouseholdDocumentCodec.Deserialize(HouseholdDocumentCodec.Serialize(document));

        Assert.True(back.ContentEquals(document));
        var playlist = back.Find(new RecordKey("playlist", "pl-1-example"))!;
        Assert.Equal(3, playlist.SchemaVersion);
        Assert.Equal("den", playlist.Fields["screen"]);
        Assert.Equal("", playlist.Fields["empty"]);
        Assert.True(back.Find(new RecordKey("veto", "robin:kestrel"))!.IsTombstone);
    }

    [Fact]
    public void AnEmptyDocumentRoundTrips() =>
        Assert.Empty(HouseholdDocumentCodec.Deserialize(HouseholdDocumentCodec.Serialize(HouseholdDocument.Empty)).Records);

    [Fact]
    public void TheFileStatesItsFormat()
    {
        var json = JsonNode.Parse(HouseholdDocumentCodec.Serialize(Sample()))!;

        Assert.Equal(HouseholdDocument.FormatVersion, (int)json["format"]!);
    }

    [Fact]
    public void TheSameDocumentAlwaysWritesTheSameText()
    {
        // Records are written in key order, so an unchanged household is an unchanged file.
        var forwards = Sample();
        var backwards = HouseholdDocument.Empty;
        foreach (var record in forwards.Records.Reverse())
        {
            backwards = backwards.With(record);
        }

        Assert.Equal(HouseholdDocumentCodec.Serialize(forwards), HouseholdDocumentCodec.Serialize(backwards));
    }

    /// <summary>
    /// A file from a newer build whose shape this build cannot read must stop it, loudly: read as best
    /// it can, merged and written back, it would be rewritten in the old shape and the newer writer's
    /// work would be gone.
    /// </summary>
    [Fact]
    public void AFutureFormatIsRefusedLoudly()
    {
        var json = JsonNode.Parse(HouseholdDocumentCodec.Serialize(Sample()))!;
        json["format"] = HouseholdDocument.FormatVersion + 1;

        var refused = Assert.Throws<HouseholdFormatException>(() => HouseholdDocumentCodec.Deserialize(json.ToJsonString()));
        Assert.Contains((HouseholdDocument.FormatVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"records\":[]}")]
    [InlineData("{\"format\":0,\"records\":[]}")]
    [InlineData("{\"format\":1}")]
    [InlineData("{\"format\":1,\"records\":[{\"kind\":\"viewer\",\"id\":\"x\",\"schema\":1,\"wall\":1,\"counter\":0,\"deleted\":false,\"fields\":{}}]}")]
    [InlineData("{\"format\":1,\"records\":[{\"kind\":\"viewer\",\"id\":\"x\",\"schema\":1,\"wall\":1,\"counter\":0,\"writer\":\"w\",\"deleted\":false,\"fields\":{\"child\":7}}]}")]
    public void AFileThatIsNotAHouseholdIsRefused(string text) =>
        Assert.Throws<HouseholdFormatException>(() => HouseholdDocumentCodec.Deserialize(text));

    [Fact]
    public void AKeyTwiceInOneFileKeepsTheWinner()
    {
        // Never written by this codec, but a hand-edited or half-merged file must still resolve one way.
        const string text = """
            {"format":1,"records":[
              {"kind":"viewer","id":"x","schema":1,"wall":5,"counter":0,"writer":"a","deleted":false,"fields":{"child":"false"}},
              {"kind":"viewer","id":"x","schema":1,"wall":9,"counter":0,"writer":"a","deleted":false,"fields":{"child":"true"}}
            ]}
            """;

        var record = Assert.Single(HouseholdDocumentCodec.Deserialize(text).Records);
        Assert.Equal("true", record.Fields["child"]);
    }
}
