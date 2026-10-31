using System.Globalization;

namespace Constitution.Domain.Time;

/// <summary>
/// The Today header date, "Tuesday, Nov 17". Lives beside the parity-held time helpers rather
/// than in them, because those copies must stay identical across Domains.
/// </summary>
public static class TodayHeader
{
    public static string Date(DateTime moment) =>
        moment.ToString("dddd, MMM d", CultureInfo.InvariantCulture);
}
