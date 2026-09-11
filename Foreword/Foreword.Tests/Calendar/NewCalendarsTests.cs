using Foreword.Domain.Calendar;
using Xunit;

namespace Foreword.Tests.Calendar;

public class NewCalendarsTests
{
    [Fact]
    public void NothingIsNewWhenNothingHasChanged()
    {
        Assert.Empty(NewCalendars.Since(["a", "b"], new HashSet<string> { "a", "b" }));
    }

    [Fact]
    public void ACalendarNobodyHasSeenIsNew()
    {
        Assert.Equal(["scouts"], NewCalendars.Since(["a", "scouts"], new HashSet<string> { "a" }));
    }

    /// <summary>
    /// On a phone that has never opened the screen, everything is new. That is right rather than
    /// noisy: the owner has never been asked about any of them.
    /// </summary>
    [Fact]
    public void EverythingIsNewTheFirstTime()
    {
        Assert.Equal(["a", "b"], NewCalendars.Since(["a", "b"], new HashSet<string>()));
    }

    [Fact]
    public void TheNewOnesKeepTheOrderTheListCameBackIn()
    {
        Assert.Equal(["b", "c"], NewCalendars.Since(["a", "b", "c"], new HashSet<string> { "a" }));
    }

    /// <summary>
    /// What is remembered is what is PRESENT, not everything ever seen. A calendar that goes away
    /// and comes back is new again — remembering it forever would mean it never was.
    /// </summary>
    [Fact]
    public void ACalendarThatGoesAwayIsNewAgainWhenItReturns()
    {
        var remembered = NewCalendars.Remember(["a", "b"]);
        var afterItLeft = NewCalendars.Remember(["a"]);

        Assert.Empty(NewCalendars.Since(["a", "b"], remembered));
        Assert.Equal(["b"], NewCalendars.Since(["a", "b"], afterItLeft));
    }
}
