using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// Mail lines (W-5.1.6, A11; mock: Mail): subject and sender only, people only (Post's own sorting, B1), newest first,
/// with when they came; never a buzz, and no unread mark (WD §1, the owner's ruling).
/// </summary>
public class MailLinesTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    [Fact]
    public void NewestFirst()
    {
        var mail = new DashMail(
        [
            new DashMailLine("m1111111111111111", "Lee", "Dentist moved to Thursday", T0.AddDays(-1)),
            new DashMailLine("m2222222222222222", "Sam", "Saturday plans", T0.AddMinutes(-20)),
            new DashMailLine("m3333333333333333", "Robin", "Photos from the game", T0.AddHours(-2)),
        ]);

        var rows = MailLines.Rows(mail, T0, Zone);

        Assert.Equal(["Saturday plans", "Photos from the game", "Dentist moved to Thursday"], rows.Select(row => row.Subject));
        Assert.Equal(["20 min", "2 h", "Yesterday"], rows.Select(row => row.When));
        Assert.Equal(["Sam", "Robin", "Lee"], rows.Select(row => row.From));
    }

    [Fact]
    public void TiesKeepPostsOrder()
    {
        var mail = new DashMail(
        [
            new DashMailLine("m1111111111111111", "Lee", "First", T0),
            new DashMailLine("m2222222222222222", "Sam", "Second", T0),
        ]);

        Assert.Equal(["First", "Second"], MailLines.Rows(mail, T0, Zone).Select(row => row.Subject));
    }

    [Fact]
    public void AMailWithNoSubjectSaysSo()
    {
        var rows = MailLines.Rows((DashMail)Messages["mail"], T0, Zone);

        Assert.Equal(DashWords.NoSubject, rows[1].Subject);
        Assert.Equal("No subject", DashWords.NoSubject);
    }

    [Fact]
    public void ALineIsItsIdSubjectSenderAndWhen()
    {
        var row = MailLines.Rows((DashMail)Messages["mail"], T0, Zone)[0];

        Assert.Equal(new MailRow("m6666666666666666", "Saturday plans", "Sam", "20 min"), row);
    }

    [Fact]
    public void NoMailIsNoRows() => Assert.Empty(MailLines.Rows(new DashMail([]), T0, Zone));
}
