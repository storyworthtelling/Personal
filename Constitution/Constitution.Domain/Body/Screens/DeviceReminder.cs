namespace Constitution.Domain.Body.Screens;

/// <summary>
/// The day after a device sat out (C-7.3.6, book page 18): today's monthly sentence says so. It is Body's
/// monthly seat on that day, not a seventh seat, and it is said once, on the day after, not every day the
/// device stays off.
/// </summary>
public static class DeviceReminder
{
    /// <summary>The device the reminder names, in the words of "No watch readings yesterday."</summary>
    public const string Watch = "watch";

    /// <summary>The signals only the watch measures. Weight comes from a scale and steps from the phone, so neither counts.</summary>
    private static readonly BodySignal[] WatchSignals = [BodySignal.RestingHeartRate, BodySignal.HeartRateVariability];

    /// <summary>
    /// "watch" when the watch read the day before yesterday and not yesterday, otherwise null. A device that
    /// has never read has nothing to be reminded of, and one that has been off for days was reminded on the first.
    /// </summary>
    public static string? SkippedDevice(IEnumerable<BodyReading> readings, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var days = readings.Where(reading => WatchSignals.Contains(reading.Signal)).Select(reading => reading.Day).ToHashSet();
        return !days.Contains(today.AddDays(-1)) && days.Contains(today.AddDays(-2)) ? Watch : null;
    }
}
