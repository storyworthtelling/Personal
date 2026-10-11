using Pulp.Domain.Calendar;

namespace Pulp.Tests.Calendar;

/// <summary>
/// The calendar's sign-in shares the voice preferences' Google account with Drive, so picking a different
/// account for the calendar would quietly repoint the vault and the voice profile. The decision is pure.
/// </summary>
public class CalendarAccountTests
{
    [Fact]
    public void TheFirstAccountChosenIsTakenAsItIs()
    {
        Assert.Equal(CalendarAccountVerdict.Accept, CalendarScreenConnection.JudgeAccount(string.Empty, "sam@example.com"));
    }

    [Fact]
    public void TheSameAccountIsAcceptedWhateverItsCase()
    {
        Assert.Equal(CalendarAccountVerdict.Accept, CalendarScreenConnection.JudgeAccount("Sam@Example.com", "sam@example.com"));
    }

    [Fact]
    public void ADifferentAccountIsRefusedInPlainWords()
    {
        Assert.Equal(CalendarAccountVerdict.Refuse, CalendarScreenConnection.JudgeAccount("sam@example.com", "other@example.com"));

        var sentence = CalendarScreenConnection.DifferentAccount("sam@example.com");
        Assert.Contains("sam@example.com", sentence, StringComparison.Ordinal);
        Assert.Contains("Drive", sentence, StringComparison.Ordinal);
    }
}
