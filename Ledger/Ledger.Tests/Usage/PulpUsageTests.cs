using Index.Domain.Usage;
using Xunit;

namespace Index.Tests.Usage;

/// <summary>
/// Pulp's usage count as Index consumes it (Pulp D90, D269–D271): parse the receipt, keep the latest
/// total per service and month, and say the one muted line under a matching charge (mockup 15). All
/// services, merchants and counts here are invented.
/// </summary>
public class PulpUsageTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    private static Dictionary<string, string> Extras(string service = "streamly", string month = "2026-09", string count = "14",
        string? last = "2026-09-18", string merchants = "Streamly|Streamly.com")
    {
        var extras = new Dictionary<string, string>
        {
            ["service"] = service, ["month"] = month, ["count"] = count, ["merchants"] = merchants,
        };
        if (last is not null)
        {
            extras["lastOpened"] = last;
        }

        return extras;
    }

    private static PulpUsageReceipt Receipt(string service, string month, int count, string? last, params string[] merchants) =>
        new(service, month, count, last is null ? null : DateOnly.Parse(last), merchants);

    // ---- parsing ----

    [Fact]
    public void AWellFormedReceiptParsesExactly()
    {
        var parsed = PulpUsageReceipt.TryParse(key => Extras().GetValueOrDefault(key), out var receipt);

        Assert.True(parsed);
        Assert.Equal("streamly", receipt!.Service);
        Assert.Equal("2026-09", receipt.YearMonth);
        Assert.Equal(14, receipt.Count);
        Assert.Equal(new DateOnly(2026, 9, 18), receipt.LastOpened);
        Assert.Equal(["Streamly", "Streamly.com"], receipt.Merchants);
    }

    [Theory]
    [InlineData("service", null)]
    [InlineData("month", "September")]
    [InlineData("month", null)]
    [InlineData("count", "many")]
    [InlineData("count", "-3")]
    [InlineData("count", "1.5")]
    [InlineData("merchants", "")]
    public void AReceiptMissingOrGarblingAFieldIsRefusedNotGuessed(string field, string? value)
    {
        var extras = Extras();
        if (value is null)
        {
            extras.Remove(field);
        }
        else
        {
            extras[field] = value;
        }

        Assert.False(PulpUsageReceipt.TryParse(key => extras.GetValueOrDefault(key), out _));
    }

    [Fact]
    public void ADatelessReceiptIsFineAndAGarbledDateIsTreatedAsNone()
    {
        Assert.True(PulpUsageReceipt.TryParse(key => Extras(last: null).GetValueOrDefault(key), out var none));
        Assert.Null(none!.LastOpened);
        Assert.True(PulpUsageReceipt.TryParse(key => Extras(last: "yesterday").GetValueOrDefault(key), out var garbled));
        Assert.Null(garbled!.LastOpened);
    }

    // ---- the ledger of received months ----

    [Fact]
    public void ALaterReceiptForTheSameServiceAndMonthReplacesTheEarlierOne()
    {
        var ledger = PulpUsageLedger.Empty
            .Apply(Receipt("streamly", "2026-09", 3, "2026-09-02", "Streamly"))
            .Apply(Receipt("streamly", "2026-09", 14, "2026-09-18", "Streamly"));

        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(14, entry.Count);
    }

    [Fact]
    public void AReceiptCountingZeroRemovesThatServiceAndMonthSoAnUndoneHandOffLeavesNoStaleCount()
    {
        var ledger = PulpUsageLedger.Empty
            .Apply(Receipt("streamly", "2026-09", 1, "2026-09-02", "Streamly"))
            .Apply(Receipt("northstar", "2026-09", 4, "2026-09-05", "Northstar+"))
            .Apply(Receipt("streamly", "2026-09", 0, null, "Streamly"));

        var kept = Assert.Single(ledger.Entries);
        Assert.Equal("northstar", kept.Service);
        Assert.Null(PulpUsageLine.For(ledger, "Streamly", "2026-09", Today));
    }

    [Fact]
    public void AZeroReceiptForAMonthNeverSeenIsHarmless()
    {
        var ledger = PulpUsageLedger.Empty.Apply(Receipt("streamly", "2026-09", 0, null, "Streamly"));

        Assert.Empty(ledger.Entries);
    }

    [Fact]
    public void AZeroCountParsesAsAReceiptNotARefusal()
    {
        Assert.True(PulpUsageReceipt.TryParse(key => Extras(count: "0", last: null).GetValueOrDefault(key), out var receipt));
        Assert.Equal(0, receipt!.Count);
    }

    [Fact]
    public void TheLedgerRoundTripsThroughItsCodecAndGarbageReadsAsEmpty()
    {
        var ledger = PulpUsageLedger.Empty.Apply(Receipt("streamly", "2026-09", 14, "2026-09-18", "Streamly", "Streamly.com"));

        static IEnumerable<string> Shape(PulpUsageLedger value) => value.Entries.Select(
            entry => $"{entry.Service}/{entry.YearMonth}/{entry.Count}/{entry.LastOpened:yyyy-MM-dd}/{string.Join(PulpUsageReceipt.MerchantSeparator, entry.Merchants)}");

        Assert.Equal(["streamly/2026-09/14/2026-09-18/Streamly|Streamly.com"], Shape(PulpUsageCodec.Decode(PulpUsageCodec.Encode(ledger))));
        Assert.Empty(PulpUsageCodec.Decode("{not json").Entries);
        Assert.Empty(PulpUsageCodec.Decode(null).Entries);
    }

    // ---- the line under a charge (mockup 15) ----

    private static PulpUsageLedger Mock() => PulpUsageLedger.Empty
        .Apply(Receipt("streamly", "2026-09", 14, "2026-09-18", "Streamly"))
        .Apply(Receipt("northstar", "2026-09", 9, "2026-09-11", "Northstar+"))
        .Apply(Receipt("lanternbox", "2026-06", 4, "2026-06-28", "Lanternbox"));

    [Fact]
    public void AMatchingChargeSaysHowManyTimesItWasOpenedThisMonth()
    {
        Assert.Equal("Opened 14 times in Pulp", PulpUsageLine.For(Mock(), "Streamly", "2026-09", Today));
        Assert.Equal("Opened 9 times in Pulp", PulpUsageLine.For(Mock(), "Northstar+", "2026-09", Today));
    }

    [Fact]
    public void OneOpeningIsSingular()
    {
        var ledger = PulpUsageLedger.Empty.Apply(Receipt("streamly", "2026-09", 1, "2026-09-02", "Streamly"));

        Assert.Equal("Opened 1 time in Pulp", PulpUsageLine.For(ledger, "Streamly", "2026-09", Today));
    }

    [Fact]
    public void AServiceUnopenedForTwoMonthsOrMoreSaysSinceWhenInTheSameMutedInk()
    {
        Assert.Equal("Not opened in Pulp since Jun 28", PulpUsageLine.For(Mock(), "Lanternbox", "2026-09", Today));
    }

    [Fact]
    public void AServiceUnopenedForOnlyOneMonthSaysNothing()
    {
        var ledger = PulpUsageLedger.Empty.Apply(Receipt("lanternbox", "2026-08", 2, "2026-08-30", "Lanternbox"));

        Assert.Null(PulpUsageLine.For(ledger, "Lanternbox", "2026-09", Today));
    }

    [Fact]
    public void ThreeCalendarMonthsAfterTheLastOpeningTheSinceLineStandsEvenIfTheDayIsEarlierInTheMonth()
    {
        // Jul 31 to Sep 1 is two calendar months on, so the line shows.
        var ledger = PulpUsageLedger.Empty.Apply(Receipt("lanternbox", "2026-07", 1, "2026-07-31", "Lanternbox"));

        Assert.Equal("Not opened in Pulp since Jul 31", PulpUsageLine.For(ledger, "Lanternbox", "2026-09", new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void AChargePulpDoesNotKnowGetsNoLine()
    {
        Assert.Null(PulpUsageLine.For(Mock(), "Cloud storage", "2026-09", Today));
    }

    [Fact]
    public void AKnownServiceNeverReceivedAnOpeningGetsNoLineRatherThanAGuessedSince()
    {
        var ledger = PulpUsageLedger.Empty.Apply(Receipt("streamly", "2026-09", 0, null, "Streamly"));

        Assert.Null(PulpUsageLine.For(ledger, "Streamly", "2026-09", Today));
    }

    [Fact]
    public void MatchingIsWholeNameAndWithoutCaseNeverAPrefix()
    {
        Assert.Equal("Opened 14 times in Pulp", PulpUsageLine.For(Mock(), "STREAMLY", "2026-09", Today));
        Assert.Null(PulpUsageLine.For(Mock(), "Streamly Extra Storage", "2026-09", Today));
    }

    [Fact]
    public void ThePastMonthsLineIsOnlyTheCountNeverASinceLine()
    {
        Assert.Null(PulpUsageLine.For(Mock(), "Lanternbox", "2026-08", Today));
        Assert.Equal("Opened 4 times in Pulp", PulpUsageLine.For(Mock(), "Lanternbox", "2026-06", Today));
    }
}
