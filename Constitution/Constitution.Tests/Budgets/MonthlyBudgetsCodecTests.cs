using System.Text.Json;
using Constitution.Domain.Budgets;

namespace Constitution.Tests.Budgets;

public class MonthlyBudgetsCodecTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // The document as a phone holds it. Typed out, not encoded, so a rename of a property cannot rename it here too.
    private const string Frozen =
        "{\"version\":1,\"months\":[{\"month\":\"2026-10\",\"budget\":\"600.00\",\"spent\":\"412.50\",\"receivedAt\":\"2026-10-24T12:00:00.0000000+00:00\"}]}";

    [Fact]
    public void ARoundTripKeepsEveryDigit()
    {
        var budgets = MonthlyBudgets.From(
        [
            new BudgetEntry("2026-10", 123456789012345.67m, 412.50m, Noon),
            new BudgetEntry("2026-09", 590.00m, 0m, Noon.AddDays(-30)),
        ]);

        var back = MonthlyBudgetsCodec.Decode(MonthlyBudgetsCodec.Encode(budgets));

        Assert.Equal(budgets.Entries, back.Entries);
        Assert.Equal("412.50", BudgetPayload.FormatAmount(back.Find("2026-10")!.Spent));
    }

    [Fact]
    public void ThePropertyNamesAreFrozen()
    {
        var text = MonthlyBudgetsCodec.Encode(MonthlyBudgets.From([new BudgetEntry("2026-10", 600.00m, 412.50m, Noon)]));

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(["version", "months"], root.EnumerateObject().Select(property => property.Name));
        var month = root.GetProperty("months")[0];
        Assert.Equal(["month", "budget", "spent", "receivedAt"], month.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.String, month.GetProperty("budget").ValueKind);
        Assert.Equal("600.00", month.GetProperty("budget").GetString());
        Assert.Equal("412.50", month.GetProperty("spent").GetString());
    }

    [Fact]
    public void ADocumentAsFrozenStillReads()
    {
        var entry = Assert.Single(MonthlyBudgetsCodec.Decode(Frozen).Entries);

        Assert.Equal(new BudgetEntry("2026-10", 600.00m, 412.50m, Noon), entry);
    }

    [Fact]
    public void NothingEncodesToAnEmptyList()
    {
        var back = MonthlyBudgetsCodec.Decode(MonthlyBudgetsCodec.Encode(MonthlyBudgets.None));

        Assert.Empty(back.Entries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"months\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"months\":[{\"month\":\"2026-13\",\"budget\":\"1.00\",\"spent\":\"1.00\",\"receivedAt\":\"2026-10-24T12:00:00.0000000+00:00\"}]}")]
    [InlineData("{\"version\":1,\"months\":[{\"month\":\"2026-10\",\"budget\":\"-1.00\",\"spent\":\"1.00\",\"receivedAt\":\"2026-10-24T12:00:00.0000000+00:00\"}]}")]
    [InlineData("{\"version\":1,\"months\":[{\"month\":\"2026-10\",\"budget\":600.0,\"spent\":\"1.00\",\"receivedAt\":\"2026-10-24T12:00:00.0000000+00:00\"}]}")]
    [InlineData("{\"version\":1,\"months\":[{\"month\":\"2026-10\",\"budget\":\"1.00\",\"spent\":\"1.00\",\"receivedAt\":\"yesterday\"}]}")]
    [InlineData("{\"version\":1,\"months\":[{\"month\":\"2026-10\",\"budget\":\"1.00\",\"spent\":\"1.00\"}]}")]
    public void AnUnreadableRecordIsRefusedWhole(string text)
    {
        Assert.Throws<FormatException>(() => MonthlyBudgetsCodec.Decode(text));
    }

    [Fact]
    public void TwoEntriesForOneMonthAreRefused()
    {
        const string entry = "{\"month\":\"2026-10\",\"budget\":\"1.00\",\"spent\":\"1.00\",\"receivedAt\":\"2026-10-24T12:00:00.0000000+00:00\"}";

        Assert.Throws<FormatException>(() => MonthlyBudgetsCodec.Decode("{\"version\":1,\"months\":[" + entry + "," + entry + "]}"));
    }
}
