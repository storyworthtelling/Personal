namespace Constitution.Domain.Voice;

/// <summary>
/// The plain Serif sentence each seat shows when there is no voice (book p.18, the neutral fallbacks),
/// built from the very facts the voiced line is given: one input, two renderings, so the plain page
/// and the voiced page cannot describe two different days. Never the calligraphy hand; the caller
/// sets it in Serif because <see cref="SeatLine.IsVoiced"/> is false.
///
/// <para>Wording is the book's, character for character, and is guarded against it
/// (<c>SeatFactsTests.FallbacksMatchPageEighteen</c>). It states what the facts state and never
/// softens a worsening.</para>
/// </summary>
public static class SeatFallbacks
{
    public static string For(TodayFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.Dinner);
        var kind = SeatWords.Named(facts.WorkoutKind, "the workout's kind");
        return $"{SeatWords.Sentence(kind)} at {SeatWords.Clock(facts.WorkoutStart)}. {SeatWords.DinnerSentence(facts.Dinner)}";
    }

    public static string For(RestDayFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.Dinner);
        return $"A rest day. {SeatWords.DinnerSentence(facts.Dinner)}";
    }

    public static string For(BodyMonthFacts facts)
    {
        var month = BodyMonth.Read(facts);
        if (month.SkippedDevice is not null)
        {
            return $"No {month.SkippedDevice} readings yesterday.";
        }

        var sentences = month.Movers.Select(mover => $"{mover.Name} {mover.Verb} {mover.Direction} this month.").ToList();
        if (month.Steady.Count > 0)
        {
            sentences.Add(
                month.Movers.Count > 0 ? "The other signals are close to last month."
                : month.Steady.Count == 1 ? $"{month.Steady[0].Name} {month.Steady[0].Verb} close to last month."
                : "All signals are close to last month.");
        }

        return string.Join(" ", sentences);
    }

    public static string For(SignalFacts facts)
    {
        var reading = SignalReading.Read(facts);
        var against = reading.Gap is null
            ? $"about the same as {reading.PastLabel}'s average"
            : $"{reading.Gap} {(reading.Latest < reading.Past ? "less" : "more")} than {reading.PastLabel}'s average";
        return $"{reading.Name}: {reading.LatestText} {reading.LatestWhen}, {against}.";
    }

    public static string For(ProgressionFacts facts)
    {
        var load = Progression.Read(facts)
            ?? throw new ArgumentException("There is no progression to report: the load is not up on last time.", nameof(facts));
        return $"{load.Today} {load.Unit} today, up from {load.Last}.";
    }

    public static string For(MealWeekFacts facts)
    {
        var week = MealWeek.Read(facts);
        var recipes = week.NewDays.Count == 0
            ? "No new recipes this week."
            : $"{SeatWords.CountWord(week.NewDays.Count)} new {(week.NewDays.Count == 1 ? "recipe" : "recipes")}, {SeatWords.Days(week.NewDays)}.";
        return $"Dinners for the week of {SeatWords.ShortDate(week.WeekOf)}. {recipes}";
    }
}
