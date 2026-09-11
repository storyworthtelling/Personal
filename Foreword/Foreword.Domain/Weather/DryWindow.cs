namespace Foreword.Domain.Weather;

/// <summary>
/// The longest stretch of today with no rain in it — the green band on the weather page.
///
/// <para>It is there because it is the question the page is actually asked. "Will it rain" is
/// answered hour by hour by the bars; "when can the lawn be cut" is not, and reading it off
/// seventeen bars is exactly the work the paper is supposed to have already done.</para>
/// </summary>
public static class DryWindow
{
    /// <summary>At or below this chance of rain, an hour counts as dry.</summary>
    public const int DryPercent = 20;

    /// <summary>A window has to be long enough to do something in. One hour is not.</summary>
    public const int MinimumHours = 2;

    /// <summary>
    /// The longest run rather than the first: a gap in the morning is not when anyone cuts the
    /// lawn, and offering it would be worse than offering nothing. A day with no run long enough
    /// returns null and the band is not drawn — a "driest hour" dressed up as a dry window would be
    /// a lie the page told with a straight face.
    /// </summary>
    public static DryHours? Find(IReadOnlyList<HourWeather> hours)
    {
        DryHours? best = null;
        var bestLength = 0;

        var runStart = -1;
        for (var i = 0; i <= hours.Count; i++)
        {
            var dry = i < hours.Count && hours[i].PrecipitationPercent <= DryPercent;

            if (dry)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }

                continue;
            }

            if (runStart >= 0)
            {
                var length = i - runStart;
                if (length >= MinimumHours && length > bestLength)
                {
                    bestLength = length;
                    best = new DryHours(hours[runStart].Hour, hours[i - 1].Hour);
                }

                runStart = -1;
            }
        }

        return best;
    }
}
