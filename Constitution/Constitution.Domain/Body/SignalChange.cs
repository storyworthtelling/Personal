namespace Constitution.Domain.Body;

/// <summary>
/// How a signal's current stretch compares with the owner's <em>own</em> stretch just before it: the
/// average of the days read in the window, against the average of the equal window before that. There
/// is nobody else in the comparison and no percentage; <see cref="Words"/> says it in the signal's units.
/// </summary>
public sealed record SignalChange
{
    /// <param name="signal">Which signal.</param>
    /// <param name="current">The average over the current window, in the signal's canonical unit.</param>
    /// <param name="previous">The average over the window before it.</param>
    /// <exception cref="ArgumentOutOfRangeException">The signal is not one of Body's.</exception>
    /// <exception cref="ArgumentException">Either average is not a real number.</exception>
    public SignalChange(BodySignal signal, double current, double previous)
    {
        if (!Enum.IsDefined(signal))
        {
            throw new ArgumentOutOfRangeException(nameof(signal), signal, "That is not one of Body's signals.");
        }

        if (!double.IsFinite(current) || !double.IsFinite(previous))
        {
            throw new ArgumentException($"A change in {BodySignals.Name(signal)} needs real numbers for both stretches.");
        }

        Signal = signal;
        Current = current;
        Previous = previous;
    }

    public BodySignal Signal { get; }

    /// <summary>The current window's average.</summary>
    public double Current { get; }

    /// <summary>The previous window's average.</summary>
    public double Previous { get; }

    /// <summary>Current minus previous: positive means it rose.</summary>
    public double Change => Current - Previous;

    /// <summary>
    /// How big the move is in steady bands (the smallest move worth calling a move, the same band the
    /// voice seats use), so a pound of weight and a thousand steps can be set side by side. 1 or more is a move.
    /// </summary>
    public double Movement => Math.Abs(Change) / BodySignals.SteadyBand(Signal);

    /// <summary>True when the move is inside a wobble: "about the same".</summary>
    public bool IsSteady => Movement < 1;

    /// <summary>
    /// True when it moved, and the way it moved is the worse way for this signal (D70: a rising weight,
    /// resting heart rate or body fat; a falling sleep, HRV, steps, active minutes or strength). A steady
    /// signal and a neutral one are never worsening.
    /// </summary>
    public bool IsWorsening => !IsSteady && BodySignals.Rising(Signal) switch
    {
        RisingIs.Worse => Change > 0,
        RisingIs.Better => Change < 0,
        _ => false,
    };

    /// <summary>The change in plain words: "down about 40 minutes a night".</summary>
    public string Words => ChangeWords.Phrase(this);

    /// <summary>The most days of reading a stretch is ever asked for before it is compared (S4).</summary>
    public const int EnoughDaysPerStretch = 7;

    /// <summary>
    /// The days of reading each stretch needs before it is compared: seven, or half the window where that is
    /// fewer. One night set against a month of nights is not a change.
    /// </summary>
    public static int MinimumDays(int windowDays) => Math.Min(EnoughDaysPerStretch, (Math.Max(1, windowDays) + 1) / 2);

    /// <summary>
    /// The change for <paramref name="signal"/> from <paramref name="readings"/>, or null when the owner
    /// has too few days of reading in one of the two stretches (<see cref="MinimumDays"/>: a fresh install
    /// has no earlier stretch yet, one night is not a month, and a change is never invented). A day still
    /// being counted (today's steps and active minutes) is not a reading yet. Readings of other signals are ignored; a later reading of the same
    /// day replaces an earlier one; the days of each stretch are averaged. The stretches are the
    /// <paramref name="window"/>'s days ending <paramref name="today"/>, and the same number before.
    /// </summary>
    /// <exception cref="ArgumentException">A reading of this signal is not a real number.</exception>
    public static SignalChange? From(IEnumerable<BodyReading> readings, BodySignal signal, BodyWindow window, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var days = BodyWindows.Days(window);
        var currentStart = today.AddDays(1 - days);
        var previousStart = today.AddDays(1 - (2 * days));

        var byDay = new Dictionary<DateOnly, double>();
        foreach (var reading in readings.Where(reading => reading.Signal == signal))
        {
            if (!double.IsFinite(reading.Value))
            {
                throw new ArgumentException($"A {BodySignals.Name(signal)} reading on {reading.Day:yyyy-MM-dd} is not a real number.", nameof(readings));
            }

            if (BodySignals.IsDayTotal(signal) && reading.Day >= today)
            {
                continue;
            }

            byDay[reading.Day] = reading.Value;
        }

        var current = byDay.Where(pair => pair.Key >= currentStart && pair.Key <= today).Select(pair => pair.Value).ToList();
        var previous = byDay.Where(pair => pair.Key >= previousStart && pair.Key < currentStart).Select(pair => pair.Value).ToList();
        var needed = MinimumDays(days);
        return current.Count < needed || previous.Count < needed
            ? null
            : new SignalChange(signal, current.Average(), previous.Average());
    }
}
