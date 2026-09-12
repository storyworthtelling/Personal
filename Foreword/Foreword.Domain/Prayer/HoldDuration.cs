namespace Foreword.Domain.Prayer;

/// <summary>
/// How long a request has been held, in the largest unit that still reads as a length of time.
///
/// <para>This is the one fact on the prayer page that the paper knows and the owner would not.
/// "Held 6 weeks" is the whole argument for printing the list rather than remembering it.</para>
/// </summary>
public static class HoldDuration
{
    private const int Fortnight = 14;
    private const int Quarter = 90;
    private const int Year = 365;

    public static string Describe(DateOnly held, DateOnly today)
    {
        var days = today.DayNumber - held.DayNumber;

        // A stored date in the future is a wrong value, not a request from tomorrow. There is no
        // way to print "held -3 days" that beats saying nothing.
        if (days <= 0)
        {
            return "added today";
        }

        if (days == 1)
        {
            return "held 1 day";
        }

        if (days < Fortnight)
        {
            return $"held {days} days";
        }

        if (days < Quarter)
        {
            return $"held {days / 7} weeks";
        }

        if (days < Year)
        {
            return $"held {days / 30} months";
        }

        var years = days / Year;
        return years == 1 ? "held 1 year" : $"held {years} years";
    }
}
