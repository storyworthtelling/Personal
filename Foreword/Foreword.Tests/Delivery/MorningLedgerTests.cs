using Foreword.Domain.Delivery;
using Xunit;

namespace Foreword.Tests.Delivery;

/// <summary>
/// The morning's four documents, each delivered at most once a day (audit 11 §7). The reMarkable upload always makes a
/// new document, so a retry after the brief landed — or after a timeout the server had in fact accepted — put a second
/// "Morning Brief" on the tablet. The ledger remembers what reached the tablet today, and a retry sends only the rest.
/// </summary>
public class MorningLedgerTests
{
    private static readonly DateOnly Today = new(2026, 6, 2);

    [Fact]
    public void NothingIsDeliveredOnAFreshMorning()
    {
        var ledger = MorningLedger.Read(null, Today);

        Assert.All(Enum.GetValues<MorningDocument>(), document => Assert.False(ledger.Delivered(document)));
    }

    [Fact]
    public void ADeliveredDocumentIsRememberedForTheRestOfTheDay()
    {
        var stored = MorningLedger.Read(null, Today).With(MorningDocument.Brief).Write();

        var again = MorningLedger.Read(stored, Today);
        Assert.True(again.Delivered(MorningDocument.Brief));
        Assert.False(again.Delivered(MorningDocument.Day));
    }

    [Fact]
    public void YesterdaysDeliveriesDoNotCountToday()
    {
        var stored = MorningLedger.Read(null, Today.AddDays(-1)).With(MorningDocument.Brief).With(MorningDocument.Day).Write();

        var today = MorningLedger.Read(stored, Today);
        Assert.False(today.Delivered(MorningDocument.Brief));
        Assert.False(today.Delivered(MorningDocument.Day));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbled")]
    [InlineData("2026-06-02|Brief,Nonsense")]
    public void AStoredValueItCannotReadIsTakenAsNothingDeliveredOrOnlyWhatItCanName(string stored)
    {
        var ledger = MorningLedger.Read(stored, Today);

        Assert.False(ledger.Delivered(MorningDocument.Day));
        Assert.Equal(stored.EndsWith("Nonsense", StringComparison.Ordinal), ledger.Delivered(MorningDocument.Brief));
    }
}
