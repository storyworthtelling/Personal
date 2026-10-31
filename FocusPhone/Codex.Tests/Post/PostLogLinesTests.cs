using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// Post's lines for the log loop (plan §4.5, G10): the SMS role on every launch, send and download results,
// the mail route, and a filing delete that keeps failing. Pure, so their wording is fixed here — the
// reader's digest groups on it — and the codes are read from Mono.Android's SmsResultError/MmsError.
public class PostLogLinesTests
{
    [Fact]
    public void TheRoleIsStatedOnEveryLaunchWithWhatTheOwnerWasTold()
    {
        Assert.Equal("SMS ROLE Held", PostLogLines.SmsRole(SmsRoleStatus.Held, SmsRoleNotice.Silent));
        Assert.Equal(
            "SMS ROLE Available — lost since the last launch; told the owner: Texts stopped",
            PostLogLines.SmsRole(SmsRoleStatus.Available, new SmsRoleNotice(true, true, true, "Texts stopped", "detail")));
        Assert.Equal(
            "SMS ROLE Available — told the owner: Post is not the texting app",
            PostLogLines.SmsRole(SmsRoleStatus.Available, new SmsRoleNotice(true, true, false, "Post is not the texting app", "d")));
    }

    [Theory]
    [InlineData(-1, "SMS SENT ok")]
    [InlineData(1, "SMS SEND FAILED: GENERIC_FAILURE (1)")]
    [InlineData(2, "SMS SEND FAILED: RADIO_OFF (2)")]
    [InlineData(3, "SMS SEND FAILED: NULL_PDU (3)")]
    [InlineData(4, "SMS SEND FAILED: NO_SERVICE (4)")]
    [InlineData(5, "SMS SEND FAILED: LIMIT_EXCEEDED (5)")]
    [InlineData(6, "SMS SEND FAILED: FDN_CHECK_FAILURE (6)")]
    [InlineData(7, "SMS SEND FAILED: SHORT_CODE_NOT_ALLOWED (7)")]
    [InlineData(8, "SMS SEND FAILED: SHORT_CODE_NEVER_ALLOWED (8)")]
    [InlineData(17, "SMS SEND FAILED: code 17")]
    public void SmsSendResultsAreNamed(int code, string line)
    {
        Assert.Equal(line, PostLogLines.SmsSent(code));
    }

    [Theory]
    [InlineData(-1, "ok")]
    [InlineData(1, "FAILED: UNSPECIFIED (1)")]
    [InlineData(2, "FAILED: INVALID_APN (2)")]
    [InlineData(3, "FAILED: UNABLE_CONNECT_MMS (3)")]
    [InlineData(4, "FAILED: HTTP_FAILURE (4)")]
    [InlineData(5, "FAILED: IO_ERROR (5)")]
    [InlineData(6, "FAILED: RETRY (6)")]
    [InlineData(7, "FAILED: CONFIGURATION_ERROR (7)")]
    [InlineData(8, "FAILED: NO_DATA_NETWORK (8)")]
    [InlineData(9, "FAILED: INVALID_SUBSCRIPTION_ID (9)")]
    [InlineData(10, "FAILED: INACTIVE_SUBSCRIPTION (10)")]
    [InlineData(11, "FAILED: DATA_DISABLED (11)")]
    [InlineData(12, "FAILED: MMS_DISABLED_BY_CARRIER (12)")]
    [InlineData(40, "FAILED: code 40")]
    public void MmsSendAndDownloadResultsAreNamed(int code, string outcome)
    {
        Assert.Equal("MMS SEND " + outcome, PostLogLines.MmsSent(code));
        Assert.Equal("MMS DOWNLOAD " + outcome, PostLogLines.MmsDownloaded(code));
    }

    [Fact]
    public void TheMailRouteIsNamed()
    {
        Assert.Equal("MAIL ROUTE GmailApi", PostLogLines.MailRoute(MailTransport.GmailApi));
        Assert.Equal("MAIL ROUTE None", PostLogLines.MailRoute(MailTransport.None));
    }

    [Fact]
    public void AFilingDeleteThatKeepsFailingSaysHowManyTimes()
    {
        Assert.Null(PostLogLines.OwedDeleteFailing(Tombstones.QuietAttempts - 1));
        Assert.Equal(
            "FILING the delete at the source has failed 3 times; the item stays filed",
            PostLogLines.OwedDeleteFailing(3));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(20, true)]
    public void AfterTheFirstLineItRepeatsOnlyEveryTenthTrySoHalfHourlySyncsDoNotFloodTheLog(int attempts, bool speaks)
    {
        Assert.Equal(speaks, PostLogLines.OwedDeleteFailing(attempts) is not null);
    }
}
