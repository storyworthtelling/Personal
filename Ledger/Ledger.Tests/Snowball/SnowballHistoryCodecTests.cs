using System.Text.Json;
using System.Text.Json.Nodes;
using Index.Domain.Model;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>The history document's shape, decided in one place for the Drive source and the sealing tool.
/// Every refusal names a position, never a value — the error text may end up in a log or a terminal.</summary>
public class SnowballHistoryCodecTests
{
    private static JsonObject Doc() => (JsonObject)JsonNode.Parse(SyntheticSnowballHistory.Json)!;

    private static string Text(JsonObject doc) => doc.ToJsonString();

    [Fact]
    public void TheSyntheticHistoryParsesToExactlyWhatIsInTheFile()
    {
        var result = SnowballHistoryCodec.Parse(SyntheticSnowballHistory.Json);

        Assert.True(result.Ok, result.Error);
        var data = result.Data!;
        Assert.Equal(SyntheticSnowballHistory.Weeks, data.Weeks);
        Assert.Equal(SyntheticSnowballHistory.LoanNames, data.Loans);
        Assert.Equal(
            SyntheticSnowballHistory.Rows,
            data.Rows.Select(r => (r.Loan, r.Week, r.Balance)).ToList());
        Assert.Equal(SyntheticSnowballHistory.LastWeek, data.LastWeek);
    }

    [Fact]
    public void BalancesAreDecimalsReadExactlyFromTheText()
    {
        var doc = Doc();
        doc["rows"]![0]!["balance"] = JsonNode.Parse("1234.56");

        var data = SnowballHistoryCodec.Parse(Text(doc)).Data!;

        Assert.Equal(1234.56m, data.Rows[0].Balance);
    }

    [Fact]
    public void TheSourceNoteIsOptional()
    {
        var doc = Doc();
        doc.Remove("source");

        Assert.True(SnowballHistoryCodec.Parse(Text(doc)).Ok);
    }

    public static TheoryData<string, Action<JsonObject>> Refusals => new()
    {
        { "no weeks", d => d["weeks"] = new JsonArray() },
        { "no loans", d => d["loans"] = new JsonArray() },
        { "no rows", d => d["rows"] = new JsonArray() },
        { "weeks missing", d => d.Remove("weeks") },
        { "loans missing", d => d.Remove("loans") },
        { "rows missing", d => d.Remove("rows") },
        { "weeks not an array", d => d["weeks"] = "2024-01-05" },
        { "a week that is not a date", d => ((JsonArray)d["weeks"]!)[3] = "last Friday" },
        { "a week in the wrong format", d => ((JsonArray)d["weeks"]!)[3] = "05/01/2024" },
        { "a blank loan name", d => ((JsonArray)d["loans"]!)[2] = " " },
        { "a loan listed twice", d => ((JsonArray)d["loans"]!)[2] = ((JsonArray)d["loans"]!)[1]!.GetValue<string>() },
        { "a week listed twice", d => ((JsonArray)d["weeks"]!)[4] = ((JsonArray)d["weeks"]!)[3]!.GetValue<string>() },
        { "a row with an unknown loan", d => d["rows"]![5]!["loan"] = "Not A Listed Loan" },
        { "a row with an unlisted week", d => d["rows"]![5]!["week"] = "1999-01-01" },
        { "a row with a negative balance", d => d["rows"]![5]!["balance"] = -1 },
        { "a row with a text balance", d => d["rows"]![5]!["balance"] = "12.00" },
        { "a row with no balance", d => ((JsonObject)d["rows"]![5]!).Remove("balance") },
        { "a row that is not an object", d => ((JsonArray)d["rows"]!)[5] = 7 },
        { "the same loan and week twice", d => ((JsonArray)d["rows"]!)[6] = JsonNode.Parse(d["rows"]![5]!.ToJsonString()) },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void AMalformedDocumentIsRefusedWithAShapeOnlyMessage(string _, Action<JsonObject> damage)
    {
        var doc = Doc();
        damage(doc);

        var result = SnowballHistoryCodec.Parse(Text(doc));

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.All(SyntheticSnowballHistory.LoanNames, name => Assert.DoesNotContain(name, result.Error!, StringComparison.Ordinal));
        Assert.DoesNotContain("Not A Listed Loan", result.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("last Friday", result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("null")]
    [InlineData("{}")]
    public void NonObjectsAndNonsenseAreRefused(string json)
    {
        var result = SnowballHistoryCodec.Parse(json);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void ParsingNeverThrowsOnWhatItIsGiven()
    {
        foreach (var json in new[] { "{", "{\"weeks\": [1], \"loans\": [2], \"rows\": [3]}", "\u0000", "{\"weeks\":null,\"loans\":null,\"rows\":null}" })
        {
            var exception = Record.Exception(() => SnowballHistoryCodec.Parse(json));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void ALoanMayHaveFewerRowsThanWeeks()
    {
        var doc = Doc();
        var rows = (JsonArray)doc["rows"]!;
        rows.RemoveAt(0); // one loan, one week not recorded

        var result = SnowballHistoryCodec.Parse(Text(doc));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(699, result.Data!.Rows.Count);
    }

    [Fact]
    public void LastWeekIsTheLatestWeekWhateverTheOrder()
    {
        var doc = Doc();
        var weeks = (JsonArray)doc["weeks"]!;
        var reversed = weeks.Select(w => w!.GetValue<string>()).Reverse().ToList();
        doc["weeks"] = new JsonArray(reversed.Select(w => (JsonNode)JsonValue.Create(w)!).ToArray());

        Assert.Equal(SyntheticSnowballHistory.LastWeek, SnowballHistoryCodec.Parse(Text(doc)).Data!.LastWeek);
    }
}
