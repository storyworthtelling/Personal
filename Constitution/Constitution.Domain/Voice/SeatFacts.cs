namespace Constitution.Domain.Voice;

/// <summary>
/// What each of the six seats is told (book p.18): its own facts, already reduced to plain words, and
/// nothing else. A seat is never handed a raw reading, a name, an address, a calendar entry or
/// anything from the owner's faith life; the inputs below have nowhere to hold one, and
/// <c>NoFaithContentTests</c> sweeps every text these builders can produce.
///
/// <list type="bullet">
/// <item><b>Today:</b> the workout (what, when, how long) and tonight's dinner. On a rest day, that it is one, and dinner.</item>
/// <item><b>Body:</b> the month's movement stated plainly, worsening first and called worse, a rising weight included (D70). The day after a device skipped a day, the reminder replaces the month.</item>
/// <item><b>A signal:</b> the latest reading against the owner's own past average, and only that: no norm, no population.</item>
/// <item><b>Encouragement:</b> the progression, and only when there is one.</item>
/// <item><b>Meal sheet:</b> the week it is for, its new recipes, its nights out.</item>
/// </list>
///
/// <para>Each request carries the plain fallback <see cref="SeatFallbacks"/> builds from the same
/// facts, so the line is never hidden. Errors have no builder here and never will.</para>
/// </summary>
public static class SeatFacts
{
    public static SeatRequest Today(TodayFacts facts)
    {
        var fallback = SeatFallbacks.For(facts);
        var kind = facts.WorkoutKind.Trim();
        if (facts.WorkoutMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(facts), facts.WorkoutMinutes, "A workout runs for some minutes.");
        }

        return new SeatRequest(VoiceSeat.TodayTopLine,
        [
            $"Workout today: {kind} at {SeatWords.Clock(facts.WorkoutStart)} {SeatWords.Meridiem(facts.WorkoutStart)}, about {facts.WorkoutMinutes} minutes.",
            SeatWords.DinnerFact(facts.Dinner),
        ], fallback);
    }

    public static SeatRequest RestDay(RestDayFacts facts)
    {
        var fallback = SeatFallbacks.For(facts);
        return new SeatRequest(VoiceSeat.RestDayLine,
            ["Today is a rest day, with no workout.", SeatWords.DinnerFact(facts.Dinner)], fallback);
    }

    public static SeatRequest BodyMonthly(BodyMonthFacts facts)
    {
        var fallback = SeatFallbacks.For(facts);
        var month = BodyMonth.Read(facts);

        if (month.SkippedDevice is not null)
        {
            return new SeatRequest(VoiceSeat.BodyMonthly,
                [$"No {month.SkippedDevice} readings yesterday.", "Today's line has a gap because of that."], fallback);
        }

        var told = month.Movers.Select(mover =>
            $"{mover.Name} {mover.Verb} {mover.Direction} this month, which is {(mover.IsWorse ? "worse" : "better")}: "
            + $"{SeatWords.Reading(mover.Month.Unit, mover.Month.ThisMonth)} on average, "
            + $"against {SeatWords.Reading(mover.Month.Unit, mover.Month.LastMonth)} last month.").ToList();
        if (month.Steady.Count > 0)
        {
            told.Add($"Close to last month: {string.Join(", ", month.Steady.Select(steady => steady.Name))}.");
        }

        return new SeatRequest(VoiceSeat.BodyMonthly, told, fallback);
    }

    public static SeatRequest Signal(SignalFacts facts)
    {
        var fallback = SeatFallbacks.For(facts);
        var reading = SignalReading.Read(facts);

        var gap = reading.Gap is null
            ? $"That is about the same as their own {reading.PastLabel} average."
            : $"That is {reading.Gap} {(reading.Latest < reading.Past ? "less" : "more")} than their own {reading.PastLabel} average, "
              + $"which is {(reading.IsWorse ? "worse" : "better")}.";

        return new SeatRequest(VoiceSeat.SignalSentence,
        [
            $"{reading.Name}: {reading.LatestText} {reading.LatestWhen}.",
            $"Their own {reading.PastLabel} average: {reading.PastText}.",
            gap,
            "The comparison is with their own past only.",
        ], fallback);
    }

    /// <summary>The encouragement request, or null when the load is not up on last time: no news is no request.</summary>
    public static SeatRequest? WorkoutEncouragement(ProgressionFacts facts)
    {
        var load = Progression.Read(facts);
        if (load is null)
        {
            return null;
        }

        return new SeatRequest(VoiceSeat.WorkoutEncouragement,
        [
            $"Exercise: {load.Exercise}.",
            $"Load today: {load.Today} {load.Unit}, up from {load.Last} {load.Unit} last time.",
            $"That is {load.Gap} {load.Unit} more.",
        ], SeatFallbacks.For(facts));
    }

    public static SeatRequest MealSheetIntro(MealWeekFacts facts)
    {
        var fallback = SeatFallbacks.For(facts);
        var week = MealWeek.Read(facts);

        var told = new List<string>
        {
            $"The dinner sheet is for the week of {SeatWords.ShortDate(week.WeekOf)}.",
            week.NewDays.Count == 0
                ? "No new recipes this week."
                : $"{SeatWords.CountWord(week.NewDays.Count)} new {(week.NewDays.Count == 1 ? "recipe" : "recipes")}, on {SeatWords.Days(week.NewDays)}.",
        };
        if (week.OutDays.Count > 0)
        {
            told.Add($"Dinner is out on {SeatWords.Days(week.OutDays)}.");
        }

        return new SeatRequest(VoiceSeat.MealSheetIntro, told, fallback);
    }
}
