using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// The texts trail is read by the owner and shared to diagnose lost and late texts, so it
// says how and when a text came in, and never what it said or who sent it in full.
public class TextTrailTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "under a minute")]
    [InlineData(-30, "under a minute")]
    [InlineData(59, "under a minute")]
    [InlineData(240, "4 min")]
    [InlineData(3900, "1 h 5 min")]
    [InlineData(7200, "2 h")]
    [InlineData(90000, "1 d 1 h")]
    public void Lag_ReadsPlainly(int seconds, string expected)
    {
        Assert.Equal(expected, TextTrail.Lag(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Sms_OnTime_SaysHowLongItTook()
    {
        var line = TextTrail.Sms("+1 555-010-0001", Received.AddSeconds(-5), Received, parts: 1);

        Assert.Equal("SMS from …01, 1 part; sent under a minute before it reached the phone", line);
    }

    [Fact]
    public void Sms_Late_IsMarkedLate()
    {
        var line = TextTrail.Sms("5550100001", Received.AddMinutes(-6), Received, parts: 2);

        Assert.Equal("SMS from …01, 2 parts; sent 6 min before it reached the phone — LATE", line);
    }

    [Fact]
    public void Sms_NeverCarriesTheWholeNumber()
    {
        var line = TextTrail.Sms("5550100001", Received, Received, parts: 1);

        Assert.DoesNotContain("5550100001", line);
        Assert.DoesNotContain("0100", line);
    }

    [Fact]
    public void Sms_WithNoAddress_SaysSo()
    {
        Assert.StartsWith("SMS from an unknown sender,", TextTrail.Sms(null, Received, Received, 1));
    }

    [Fact]
    public void Notice_Group_SaysGroupAndCountsSenders()
    {
        var notice = new MessagingNotice("conv-42", "Book club", IsGroup: true,
        [
            new NoticeMessage("Avery Example", Received.AddMinutes(-9).ToUnixTimeMilliseconds(), "secret words"),
            new NoticeMessage("Blake Example", Received.AddMinutes(-8).ToUnixTimeMilliseconds(), "more"),
            new NoticeMessage(null, Received.AddMinutes(-7).ToUnixTimeMilliseconds(), "mine"),
        ]);

        var line = TextTrail.Notice(notice, Received);

        Assert.Equal(
            "Google Messages notification: group conversation (2 senders), 3 messages; newest sent 7 min before it was shown — LATE",
            line);
        Assert.DoesNotContain("secret", line);
        Assert.DoesNotContain("Avery", line);
        Assert.DoesNotContain("Book club", line);
    }

    [Fact]
    public void Notice_OneToOne_OnTime()
    {
        var notice = new MessagingNotice("conv-7", "Avery Example", IsGroup: false,
            [new NoticeMessage("Avery Example", Received.ToUnixTimeMilliseconds(), "hi")]);

        Assert.Equal(
            "Google Messages notification: one-to-one, 1 message; newest sent under a minute before it was shown",
            TextTrail.Notice(notice, Received));
    }

    [Fact]
    public void MmsDownloaded_NamesAGroupByItsAddressCount()
    {
        Assert.Equal("MMS downloaded: group of 4 addresses", TextTrail.MmsDownloaded(addresses: 4));
        Assert.Equal("MMS downloaded: one-to-one", TextTrail.MmsDownloaded(addresses: 2));
    }
}
