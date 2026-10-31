namespace Constitution.Domain.Workouts;

/// <summary>What falls from the sky in an hour, when anything does.</summary>
public enum PrecipitationKind
{
    /// <summary>Nothing is forecast to fall, or the forecast does not say what.</summary>
    None,

    Rain,

    Snow,

    /// <summary>Freezing rain or sleet: wet and below freezing, the worst underfoot.</summary>
    Ice,
}

/// <summary>
/// One hour of forecast at home, in the units the household reads (degrees Fahrenheit, US air quality index).
/// The hour is <c>[Start, Start + 1 hour)</c>. Plain data: the head fills it from the hourly forecast and the
/// air-quality reading, and a null <see cref="AirQualityIndex"/> says the reading was not available, which is
/// not the same as good air.
/// </summary>
/// <param name="Start">The first instant of the hour.</param>
/// <param name="TemperatureFahrenheit">The temperature the hour is expected to reach.</param>
/// <param name="PrecipitationChance">The chance of precipitation, 0 to 100 percent.</param>
/// <param name="Precipitation">What would fall; <see cref="PrecipitationKind.None"/> when it does not say.</param>
/// <param name="AirQualityIndex">The US air quality index, 0 to 500, or null when it was not read.</param>
/// <param name="IsDaylight">Whether the sun is up for the whole hour.</param>
public sealed record HourConditions(
    DateTimeOffset Start,
    double TemperatureFahrenheit,
    int PrecipitationChance,
    PrecipitationKind Precipitation,
    int? AirQualityIndex,
    bool IsDaylight)
{
    public static readonly TimeSpan Length = TimeSpan.FromHours(1);

    public DateTimeOffset End => Start + Length;
}

/// <summary>
/// What reading the forecast gave: the hours, or the plain fact that it could not be read. A forecast nobody could
/// read is not good weather, and the adapter treats it as "no change" rather than as a reason: an outdoor session was
/// planned with a reason to be there, and a failed lookup is not a reason to cancel it.
/// </summary>
public sealed class ConditionsRead
{
    private ConditionsRead(IReadOnlyList<HourConditions> hours, string? unavailableBecause)
    {
        Hours = hours;
        UnavailableBecause = unavailableBecause;
    }

    /// <summary>The hours in time order; empty when the read is unavailable.</summary>
    public IReadOnlyList<HourConditions> Hours { get; }

    /// <summary>Why nothing was read, as a plain sentence with nothing from the network in it; null when it was read.</summary>
    public string? UnavailableBecause { get; }

    public bool IsAvailable => UnavailableBecause is null;

    /// <summary>A forecast that was read. It may hold no hours at all (a window the source said nothing about).</summary>
    public static ConditionsRead Of(IEnumerable<HourConditions> hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        var kept = hours.OrderBy(hour => hour.Start).ToList();
        foreach (var hour in kept)
        {
            if (hour.PrecipitationChance is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(hours), hour.PrecipitationChance, "A chance of precipitation is a percentage, 0 to 100.");
            }

            if (hour.AirQualityIndex is < 0 or > 500)
            {
                throw new ArgumentOutOfRangeException(nameof(hours), hour.AirQualityIndex, "The air quality index runs from 0 to 500.");
            }
        }

        if (kept.Zip(kept.Skip(1)).Any(pair => pair.First.Start == pair.Second.Start))
        {
            throw new ArgumentException("Two forecast hours start at the same instant, so one of them is wrong.", nameof(hours));
        }

        return new ConditionsRead(kept.AsReadOnly(), null);
    }

    /// <summary>A forecast that could not be read, with the reason.</summary>
    public static ConditionsRead Unavailable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unavailable forecast has to say why.", nameof(reason));
        }

        return new ConditionsRead([], reason.Trim());
    }

    /// <summary>The hours that share any time with <c>[start, end)</c>, in time order.</summary>
    public IReadOnlyList<HourConditions> Overlapping(DateTimeOffset start, DateTimeOffset end) =>
        Hours.Where(hour => hour.Start < end && hour.End > start).ToList();
}

/// <summary>
/// The seam the Android head fills from the hourly forecast and the air-quality service. It does the I/O and turns every
/// failure it can name into <see cref="ConditionsRead.Unavailable"/>; an exception it does not understand is a bug and is
/// allowed to propagate. Nothing in this folder touches a network.
/// </summary>
public interface IConditionsSource
{
    /// <summary>The hours that overlap <c>[from, to)</c>. It may return more; it may not return fewer.</summary>
    Task<ConditionsRead> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation);
}

/// <summary>Why an outdoor session is not outdoors. A closed list; <see cref="ConditionsWords"/> is its only wording.</summary>
public enum ConditionCause
{
    Rain,

    Snow,

    Ice,

    Heat,

    PoorAir,

    Darkness,
}

/// <summary>
/// Where "bad" begins. Every number is the agent's reading of a beginner's comfort, not the owner's, and sits here so a
/// later settings screen can change one without touching a rule.
/// </summary>
public sealed record ConditionsLimits
{
    /// <summary>At or above this temperature an outdoor session is too hot.</summary>
    public double HeatFahrenheit { get; init; } = 90;

    /// <summary>At or below this temperature, rain is ice.</summary>
    public double FreezingFahrenheit { get; init; } = 32;

    /// <summary>At or above this chance, forecast precipitation counts.</summary>
    public int WetChancePercent { get; init; } = 50;

    /// <summary>Above this air quality index the air is poor (the index calls 101 and over unhealthy for sensitive groups).</summary>
    public int PoorAirIndex { get; init; } = 100;

    /// <summary>Whether an outdoor session needs daylight. Off, because a winter Sunday walk would otherwise rarely survive.</summary>
    public bool AvoidDarkness { get; init; }

    /// <summary>Whether a session is first tried at another time inside the gap it was found in, before it is moved indoors.</summary>
    public bool MayMoveWithinGap { get; init; } = true;

    /// <summary>How finely another start is looked for inside the gap.</summary>
    public TimeSpan MoveStep { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Room kept inside the gap after a sweaty session that is moved, so that placement (which keeps the same room) does not
    /// pull it back into the bad hour. Pass the placement option's value if the household has changed it.
    /// </summary>
    public TimeSpan ShowerRoom { get; init; } = Constitution.Domain.Placement.PlacementOptions.DefaultShowerBuffer;

    internal void Check()
    {
        if (WetChancePercent is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(WetChancePercent), WetChancePercent, "A chance that counts as wet is a percentage from 1 to 100.");
        }

        if (MoveStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MoveStep), MoveStep, "Another start has to be looked for in steps of some length.");
        }

        if (ShowerRoom < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ShowerRoom), ShowerRoom, "Room to shower cannot be negative.");
        }
    }
}

/// <summary>The single reading of an hour: which cause, if any, makes it bad for being outside.</summary>
public static class ConditionsAssessment
{
    /// <summary>
    /// The cause that makes this hour bad outdoors, or null. When several apply the first of ice, snow, rain, heat, poor air,
    /// darkness is named, so the line a card carries is always the worst underfoot first. A chance of precipitation with no
    /// type is read as rain.
    /// </summary>
    public static ConditionCause? Of(HourConditions hour, ConditionsLimits limits)
    {
        ArgumentNullException.ThrowIfNull(hour);
        ArgumentNullException.ThrowIfNull(limits);
        if (hour.PrecipitationChance >= limits.WetChancePercent)
        {
            var kind = hour.Precipitation == PrecipitationKind.None ? PrecipitationKind.Rain : hour.Precipitation;
            if (kind == PrecipitationKind.Ice || (kind == PrecipitationKind.Rain && hour.TemperatureFahrenheit <= limits.FreezingFahrenheit))
            {
                return ConditionCause.Ice;
            }

            return kind == PrecipitationKind.Snow ? ConditionCause.Snow : ConditionCause.Rain;
        }

        if (hour.TemperatureFahrenheit >= limits.HeatFahrenheit)
        {
            return ConditionCause.Heat;
        }

        if (hour.AirQualityIndex > limits.PoorAirIndex)
        {
            return ConditionCause.PoorAir;
        }

        return limits.AvoidDarkness && !hour.IsDaylight ? ConditionCause.Darkness : null;
    }

    /// <summary>The first cause that makes any hour sharing time with <c>[start, end)</c> bad, or null. Hours the forecast does not hold are not bad.</summary>
    public static ConditionCause? Of(ConditionsRead conditions, DateTimeOffset start, DateTimeOffset end, ConditionsLimits limits)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(limits);
        foreach (var hour in conditions.Overlapping(start, end))
        {
            if (Of(hour, limits) is { } cause)
            {
                return cause;
            }
        }

        return null;
    }
}
