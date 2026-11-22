using System.Security.Cryptography;
using System.Text;
using Watch.Domain.Dash;

namespace Watch.Tests.Dash;

/// <summary>
/// The ids Dash's contracts carry: Post's keys as digests, so the watch never holds a number, an address or Post's own
/// source id, and the watch's own names for its acts.
/// </summary>
public class DashIdsTests
{
    private static string Expected(char letter, string value) =>
        letter + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{letter}\n{value}")))[..16].ToLowerInvariant();

    [Fact]
    public void AnIdIsALetterAndSixteenHexDigits()
    {
        Assert.Matches("^c[0-9a-f]{16}$", DashIds.Correspondence("2125550101"));
        Assert.Matches("^m[0-9a-f]{16}$", DashIds.Message("sms:12"));
        Assert.Matches("^a[0-9a-f]{16}$", DashIds.Attachment("mms:40", "part:7"));
        Assert.Equal(16, DashIds.DigestLength);
    }

    [Fact]
    public void TheDigestIsTheFrozenOne()
    {
        // The watch keeps what it was shown by these ids, so how they are made is a wire shape.
        Assert.Equal(Expected('c', "group:mms:17"), DashIds.Correspondence("group:mms:17"));
        Assert.Equal(Expected('m', "gm:abc"), DashIds.Message("gm:abc"));
        Assert.Equal(Expected('a', "mms:40\npart:7"), DashIds.Attachment("mms:40", "part:7"));
    }

    [Fact]
    public void TheSameThingKeepsItsId()
    {
        Assert.Equal(DashIds.Correspondence("2125550101"), DashIds.Correspondence("2125550101"));
        Assert.NotEqual(DashIds.Correspondence("2125550101"), DashIds.Correspondence("2125550102"));
    }

    [Fact]
    public void KindsNeverShareADigest()
    {
        Assert.NotEqual(DashIds.Correspondence("sms:12")[1..], DashIds.Message("sms:12")[1..]);
    }

    [Fact]
    public void NoNumberIsInAnId()
    {
        Assert.DoesNotContain("5550101", DashIds.Correspondence("2125550101"), StringComparison.Ordinal);
        Assert.DoesNotContain("sam@example.com", DashIds.Message("sam@example.com"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("c0123456789abcdef", true, false, false)]
    [InlineData("m0123456789abcdef", false, true, false)]
    [InlineData("a0123456789abcdef", false, false, true)]
    [InlineData("C0123456789ABCDEF", false, false, false)]
    [InlineData("c0123456789abcde", false, false, false)]
    [InlineData("c0123456789abcdef0", false, false, false)]
    [InlineData("c0123456789abcdeg", false, false, false)]
    [InlineData("x0123456789abcdef", false, false, false)]
    [InlineData("", false, false, false)]
    [InlineData(null, false, false, false)]
    public void TheShapesAreChecked(string? id, bool correspondence, bool message, bool attachment)
    {
        Assert.Equal(correspondence, DashIds.IsCorrespondence(id));
        Assert.Equal(message, DashIds.IsMessage(id));
        Assert.Equal(attachment, DashIds.IsAttachment(id));
    }

    [Theory]
    [InlineData("act-1", true)]
    [InlineData("send-2026-11-21-a", true)]
    [InlineData("0", true)]
    [InlineData("", false)]
    [InlineData("Act-1", false)]
    [InlineData("act_1", false)]
    [InlineData("act 1", false)]
    [InlineData(null, false)]
    public void AnActIsTheWatchsOwnName(string? id, bool valid)
    {
        Assert.Equal(valid, DashIds.IsAct(id));
    }

    [Fact]
    public void AnActIsAtMostSixtyFour()
    {
        Assert.Equal(64, DashIds.MaxActLength);
        Assert.True(DashIds.IsAct(new string('a', 64)));
        Assert.False(DashIds.IsAct(new string('a', 65)));
    }
}
