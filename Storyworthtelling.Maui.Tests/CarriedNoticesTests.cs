using Storyworthtelling.Folds;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// A page that leaves as it finishes (Index's Plaid link pops to Accounts) has no corner left to fold a
/// notice into, so it hands the notice to whichever page shows next (audit W8.2b: it was an alert).
/// </summary>
public class CarriedNoticesTests
{
    private static readonly DogEarNotice Backup = new("The vault backup did not finish.", "Drive refused it.", DogEarHue.Problem);

    [Fact]
    public void ANoticeCarriedIsTakenOnceByTheNextPage()
    {
        var carried = new CarriedNotices();
        carried.Carry("vault-backup", Backup);

        var first = new DogEarState();
        carried.DeliverTo(first);
        var second = new DogEarState();
        carried.DeliverTo(second);

        Assert.Single(first.Notices);
        Assert.Empty(second.Notices);
    }

    [Fact]
    public void CarryingTheSameKeyAgainKeepsTheLatest()
    {
        var carried = new CarriedNotices();
        carried.Carry("vault-backup", Backup);
        carried.Carry("vault-backup", Backup with { Detail = "The passphrase was refused." });

        var page = new DogEarState();
        carried.DeliverTo(page);

        Assert.Equal("The passphrase was refused.", Assert.Single(page.Notices).Detail);
    }
}
