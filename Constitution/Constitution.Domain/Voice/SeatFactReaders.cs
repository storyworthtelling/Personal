namespace Constitution.Domain.Voice;

// Each seat's inputs, checked and reduced once, so the voiced facts and the plain fallback read the
// same numbers the same way. Internal: the public surface is SeatFacts and SeatFallbacks.

/// <summary>One signal that moved: how it is named, whether it is up or down, and whether that is worse.</summary>
internal sealed record Mover(string Name, string Verb, string Direction, bool IsWorse, SignalMonth Month);

internal sealed record Steadied(string Name, string Verb);

internal sealed record BodyMonth(string? SkippedDevice, IReadOnlyList<Mover> Movers, IReadOnlyList<Steadied> Steady)
{
    /// <summary>
    /// Worse movers first, then better, each group in the order given (Body orders by what moved most,
    /// and a rising weight is already marked worsening by the caller, D70). A mover is never softened:
    /// it is up or down, and it is worse or better.
    /// </summary>
    public static BodyMonth Read(BodyMonthFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.Signals);

        string? device = null;
        if (facts.SkippedDevice is not null)
        {
            device = SeatWords.Named(facts.SkippedDevice, "the device's name");
        }
        else if (facts.Signals.Count == 0)
        {
            throw new ArgumentException(
                "Body's monthly sentence needs at least one signal, or the device that skipped a day.", nameof(facts));
        }

        var movers = new List<Mover>();
        var steady = new List<Steadied>();
        foreach (var signal in facts.Signals)
        {
            ArgumentNullException.ThrowIfNull(signal);
            var name = SeatWords.Named(signal.Name, "the signal's name");
            SeatWords.Known(signal.Unit);
            var thisMonth = SeatWords.Finite(signal.ThisMonth, name);
            var lastMonth = SeatWords.Finite(signal.LastMonth, name);
            var verb = signal.NamePlural ? "are" : "is";

            if (Math.Abs(thisMonth - lastMonth) < SeatWords.SteadyBand(signal.Unit))
            {
                steady.Add(new Steadied(name, verb));
                continue;
            }

            var rose = thisMonth > lastMonth;
            movers.Add(new Mover(name, verb, rose ? "up" : "down", rose == signal.RisingIsWorse, signal));
        }

        var ordered = movers.Where(mover => mover.IsWorse).Concat(movers.Where(mover => !mover.IsWorse)).ToList();
        return new BodyMonth(device, ordered, steady);
    }
}

internal sealed record SignalReading(
    string Name, string LatestWhen, string PastLabel, double Latest, double Past,
    string LatestText, string PastText, string? Gap, bool IsWorse)
{
    public static SignalReading Read(SignalFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var name = SeatWords.Named(facts.Name, "the signal's name");
        var when = SeatWords.Named(facts.LatestWhen, "when the reading was taken");
        var past = SeatWords.Named(facts.PastLabel, "which stretch of the owner's own past it is set against");
        SeatWords.Known(facts.Unit);
        var latest = SeatWords.Finite(facts.Latest, name);
        var average = SeatWords.Finite(facts.PastAverage, name);

        return new SignalReading(
            name, when, past, latest, average,
            SeatWords.Reading(facts.Unit, latest), SeatWords.Reading(facts.Unit, average),
            SeatWords.Gap(facts.Unit, latest, average),
            IsWorse: latest > average == facts.RisingIsWorse);
    }
}

internal sealed record Progression(string Exercise, string Today, string Last, string Unit, string Gap)
{
    /// <summary>The progression, or null when the load is not up: only an increase is news.</summary>
    public static Progression? Read(ProgressionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var exercise = SeatWords.Named(facts.Exercise, "the exercise's name");
        var unit = SeatWords.Named(facts.Unit, "the load's unit");
        var today = SeatWords.Finite(facts.LoadToday, exercise);
        var last = SeatWords.Finite(facts.LoadLast, exercise);

        return today > last
            ? new Progression(exercise, SeatWords.Number(today), SeatWords.Number(last), unit, SeatWords.Number(today - last))
            : null;
    }
}

internal sealed record MealWeek(DateOnly WeekOf, IReadOnlyList<DayOfWeek> NewDays, IReadOnlyList<DayOfWeek> OutDays)
{
    public static MealWeek Read(MealWeekFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var newDays = SeatWords.KnownDays(facts.NewRecipeDays, nameof(facts.NewRecipeDays)).Distinct().ToList();
        var outDays = SeatWords.KnownDays(facts.DinnerOutDays, nameof(facts.DinnerOutDays)).Distinct().ToList();
        return new MealWeek(facts.WeekOf, newDays, outDays);
    }
}
