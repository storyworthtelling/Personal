using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Which of a contact's numbers Post's Rolodex reaches first (P14 B6): the one the contacts app marks primary, else a
/// mobile, else the first as the contacts app lists them. The phone's Rolodex texts and calls it, and a new text from
/// the wrist goes to it, so the two can never reach two different numbers. Invented numbers.
/// </summary>
public class RolodexNumbersTests
{
    private const int Home = 1;
    private const int Work = 3;

    private static ContactNumber Number(string number, int type, bool primary = false) => new(number, primary, type);

    [Fact]
    public void ThePrimaryComesFirst()
    {
        var order = RolodexNumbers.Order(
            [Number("2125550101", Home), Number("2125550102", RolodexNumbers.MobileType), Number("2125550103", Work, primary: true)]);

        Assert.Equal(["2125550103", "2125550102", "2125550101"], order);
    }

    [Fact]
    public void AMobileComesBeforeTheRest()
    {
        var order = RolodexNumbers.Order([Number("2125550101", Home), Number("2125550103", Work), Number("2125550102", RolodexNumbers.MobileType)]);

        Assert.Equal(["2125550102", "2125550101", "2125550103"], order);
    }

    [Fact]
    public void OtherwiseTheContactsAppsOrder() =>
        Assert.Equal(["2125550103", "2125550101"], RolodexNumbers.Order([Number("2125550103", Work), Number("2125550101", Home)]));

    [Fact]
    public void AWorkMobileIsAMobile()
    {
        Assert.Equal(["2125550104", "2125550101"], RolodexNumbers.Order([Number("2125550101", Home), Number("2125550104", RolodexNumbers.WorkMobileType)]));
        Assert.True(RolodexNumbers.IsMobile(2));
        Assert.True(RolodexNumbers.IsMobile(17));
        Assert.False(RolodexNumbers.IsMobile(Home));
    }

    [Fact]
    public void ANumberListedTwiceIsOnce()
    {
        // Two raw contacts (two accounts) often list the same number; one of them may mark it primary.
        var order = RolodexNumbers.Order(
            [Number("2125550101", Home), Number("2125550102", RolodexNumbers.MobileType), Number("2125550101", Home, primary: true)]);

        Assert.Equal(["2125550101", "2125550102"], order);
    }

    [Fact]
    public void NoNumbersIsNone() => Assert.Empty(RolodexNumbers.Order([]));
}
