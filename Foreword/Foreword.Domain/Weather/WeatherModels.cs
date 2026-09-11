namespace Foreword.Domain.Weather;

/// <summary>One hour of today: what it will be, and how likely rain is in it.</summary>
/// <param name="Hour">The hour itself, local, on the hour.</param>
/// <param name="Degrees">Whole degrees. The page has no room for a decimal and nobody reads one.</param>
/// <param name="PrecipitationPercent">Chance of precipitation, 0-100.</param>
public readonly record struct HourWeather(DateTime Hour, int Degrees, int PrecipitationPercent);

/// <summary>A stretch of the day with no rain in it. <see cref="End"/> is the last dry hour.</summary>
public readonly record struct DryHours(DateTime Start, DateTime End);

/// <summary>One day of the week strip: the word for it, the two numbers, and the chance of rain.</summary>
public sealed record WeekDay(
    DateOnly Date, string Condition, int High, int Low, int PrecipitationPercent);

/// <summary>
/// One reading on the allergy strip. <paramref name="Value"/> is the Universal Pollen Index (0-5) or
/// an air-quality band mapped onto the same five steps, so one meter draws all four.
/// </summary>
public sealed record AllergyReading(string Name, string Category, int Value)
{
    /// <summary>The meter has five steps. Both scales are rendered against it.</summary>
    public const int Steps = 5;
}

/// <summary>Sunrise, sunset and the moon — on the page because smart tasks can require daylight.</summary>
public sealed record SunAndMoon(TimeOnly? Sunrise, TimeOnly? Sunset, string MoonPhase);

/// <summary>Today in detail, on the same 6am-10pm axis the timeline uses.</summary>
public sealed record TodayWeather
{
    public required DateOnly Date { get; init; }

    /// <summary>Hour by hour across the drawn day. Empty when nothing was fetched.</summary>
    public IReadOnlyList<HourWeather> Hours { get; init; } = [];

    public int High { get; init; }
    public int Low { get; init; }

    /// <summary>When the high lands, so the page can mark it. Null when the hours are missing.</summary>
    public DateTime? HighAt { get; init; }

    /// <summary>The day's word — "Clearing", "Sunny". From the daytime forecast.</summary>
    public string Condition { get; init; } = string.Empty;

    public SunAndMoon Sun { get; init; } = new(null, null, string.Empty);
}

/// <summary>
/// Everything D2 draws. Assembled from four separate Maps Platform APIs, each of which can fail on
/// its own — so every part is optional and the page draws what came back. A morning with no
/// forecast still has a calendar on it, and the document goes out either way.
/// </summary>
public sealed record WeatherReport
{
    public TodayWeather? Today { get; init; }
    public IReadOnlyList<WeekDay> Week { get; init; } = [];
    public IReadOnlyList<AllergyReading> Allergy { get; init; } = [];

    /// <summary>Nothing came back at all — the page is skipped rather than drawn empty.</summary>
    public bool IsEmpty => Today is null && Week.Count == 0 && Allergy.Count == 0;

    public static WeatherReport Nothing { get; } = new();
}
