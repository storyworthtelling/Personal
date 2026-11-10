using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// What Plate says when a scan does not reach the reMarkable (audit 11 §21). It said "the reMarkable refused it" with the
/// raw message whatever happened — blaming the tablet for a phone with no network, and offering nothing to do next. The
/// scan is always filed under Documents first, so each sentence says so and then what to do.
/// </summary>
public class ScanSendWordsTests
{
    [Fact]
    public void NoNetworkSaysSoAndSaysToSendItAgain()
    {
        var words = ScanSendWords.NotSent(ScanSendProblem.NoNetwork, null);

        Assert.StartsWith("Saved to Documents.", words, StringComparison.Ordinal);
        Assert.Contains("no network", words, StringComparison.Ordinal);
        Assert.Contains("send it again", words, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", words, StringComparison.Ordinal);
    }

    [Fact]
    public void ATooLargeScanGivesTheTabletsOwnReasonAndNoRetry()
    {
        var words = ScanSendWords.NotSent(ScanSendProblem.TooLarge, "the PDF is 173 MB, over the tablet's 100 MB limit");

        Assert.Contains("the PDF is 173 MB, over the tablet's 100 MB limit", words, StringComparison.Ordinal);
        Assert.DoesNotContain("send it again", words, StringComparison.Ordinal);
    }

    [Fact]
    public void AnyOtherFailureNamesNoExceptionAndOffersAnotherTry()
    {
        var words = ScanSendWords.NotSent(ScanSendProblem.Failed, null);

        Assert.Contains("didn't reach the reMarkable", words, StringComparison.Ordinal);
        Assert.Contains("send it again", words, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", words, StringComparison.Ordinal);
    }
}
