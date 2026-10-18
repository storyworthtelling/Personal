namespace Constitution.Infrastructure.SleepNumber;

/// <summary>
/// One night's sleep as the bed measured it. <paramref name="Day"/> is the date the sleep ended on (the
/// morning), which is how the bed keys its day records; <paramref name="Asleep"/> is always above zero,
/// because a night the bed saw nobody in is "no reading" (null), not a zero.
/// </summary>
public sealed record BedNight(DateOnly Day, TimeSpan Asleep);
