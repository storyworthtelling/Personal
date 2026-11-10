using Foreword.Domain.Delivery;
using Xunit;

namespace Foreword.Tests.Delivery;

/// <summary>
/// What the morning says it did, one document at a time (audit 11 §6). A failed upload of The Day was swallowed and the
/// run recorded "Delivered", so the owner woke to no calendar page while Settings said it had gone. Now each document
/// reports its own outcome, the summary names what did not arrive, and the run retries while anything is missing.
/// </summary>
public class MorningOutcomeTests
{
    [Fact]
    public void AMorningWhereEverythingArrivedSaysSo()
    {
        var outcome = new MorningOutcome()
            .Delivered(MorningDocument.Brief, "8 articles + puzzle")
            .Delivered(MorningDocument.Day)
            .Delivered(MorningDocument.Prayer);

        Assert.False(outcome.AnyFailed);
        Assert.Equal("Delivered: the brief (8 articles + puzzle), The Day, prayer", outcome.Summary());
    }

    [Fact]
    public void AFailedDocumentIsNamedWithItsReasonAndTheRunRetries()
    {
        var outcome = new MorningOutcome()
            .Delivered(MorningDocument.Brief, "puzzle only")
            .Failed(MorningDocument.Day, "no network")
            .Delivered(MorningDocument.Prayer);

        Assert.True(outcome.AnyFailed);
        Assert.Equal("The Day did not arrive (no network). Delivered: the brief (puzzle only), prayer", outcome.Summary());
    }

    [Fact]
    public void ADocumentAlreadyOnTheTabletIsNotSentAgainAndCountsAsDelivered()
    {
        var outcome = new MorningOutcome().AlreadyDelivered(MorningDocument.Brief).Delivered(MorningDocument.Day);

        Assert.False(outcome.AnyFailed);
        Assert.Equal("Delivered: the brief (earlier this morning), The Day", outcome.Summary());
    }

    [Fact]
    public void ADocumentNotDueIsLeftOutOfTheSentence()
    {
        var outcome = new MorningOutcome().Delivered(MorningDocument.Brief).Skipped(MorningDocument.Bonus);

        Assert.Equal("Delivered: the brief", outcome.Summary());
    }
}
