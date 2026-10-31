using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Calendar;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What woke the meal pass.</summary>
public enum MealPassTrigger
{
    /// <summary>The hourly worker.</summary>
    Scheduled,

    /// <summary>The app was opened: whoever has just set something up is opening it to see it work, so a plan that did not settle is tried again at once.</summary>
    Opened,
}

/// <summary>
/// What the meal pass remembers between passes, so a pass with nothing due is a look at three small local files. Every moment is an instant; the week is
/// the Sunday that was planned. <see cref="PlanSettledAt"/> is set only by a run that reached a final answer (planned, or "not the planner"); a run that
/// stopped for a reason the household can mend only sets <see cref="PlanTriedAt"/>.
/// </summary>
public sealed record MealPassStamp(DateTimeOffset? PlanSettledAt, DateOnly? PlanWeek, DateTimeOffset? PlanTriedAt, DateTimeOffset? DeadlineTriedAt)
{
    public static MealPassStamp Empty { get; } = new(null, null, null, null);
}

/// <summary>What reading the stamp gave. The stamp only decides when to look again, so an unreadable one costs one extra run, never a lost fact.</summary>
public abstract record MealPassStampRead
{
    private MealPassStampRead()
    {
    }

    public sealed record NothingYet : MealPassStampRead;

    public sealed record Held(MealPassStamp Stamp) : MealPassStampRead;

    public sealed record Unreadable(string Reason) : MealPassStampRead;
}

/// <summary>Where the stamp is kept.</summary>
public interface IMealPassStamps
{
    MealPassStampRead Load();

    /// <summary>Keeps the stamp, replacing whatever was there, including a file that cannot be read.</summary>
    void Save(MealPassStamp stamp);
}

/// <summary>The stamp as JSON; the names are frozen.</summary>
public static class MealPassStampCodec
{
    public const string NotJson = "The meal pass stamp is not valid JSON.";
    public const string WrongShape = "The meal pass stamp is not in a shape this version reads.";

    public const int Version = 1;

    public static string Encode(MealPassStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            Moment(writer, "planSettledAt", stamp.PlanSettledAt);
            if (stamp.PlanWeek is { } week)
            {
                writer.WriteString("planWeek", week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNull("planWeek");
            }

            Moment(writer, "planTriedAt", stamp.PlanTriedAt);
            Moment(writer, "deadlineTriedAt", stamp.DeadlineTriedAt);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static MealPassStampRead Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new MealPassStampRead.NothingYet();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            // The sentence is the report; the parser's message can quote the text.
            return new MealPassStampRead.Unreadable(NotJson);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number)
                || number != Version
                || !TryMoment(root, "planSettledAt", out var settled)
                || !TryMoment(root, "planTriedAt", out var planTried)
                || !TryMoment(root, "deadlineTriedAt", out var deadlineTried)
                || !TryWeek(root, out var week))
            {
                return new MealPassStampRead.Unreadable(WrongShape);
            }

            return new MealPassStampRead.Held(new MealPassStamp(settled, week, planTried, deadlineTried));
        }
    }

    private static void Moment(Utf8JsonWriter writer, string name, DateTimeOffset? moment)
    {
        if (moment is { } value)
        {
            writer.WriteString(name, value.ToString("o", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static bool TryMoment(JsonElement root, string name, out DateTimeOffset? moment)
    {
        moment = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            moment = parsed;
            return true;
        }

        return false;
    }

    private static bool TryWeek(JsonElement root, out DateOnly? week)
    {
        week = null;
        if (!root.TryGetProperty("planWeek", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            week = parsed;
            return true;
        }

        return false;
    }
}

/// <summary>Which parts of the pass have something to do. <paramref name="RecipeHour"/> is a recipe in its hour for this phone's tablet that is not on it yet (<see cref="MealRecipeHour"/>).</summary>
public sealed record MealPassWork(bool PlanWeek, bool Deadline, bool RecipeHour = false)
{
    public bool Any => PlanWeek || Deadline || RecipeHour;
}

/// <summary>When the meal pass wakes and how often it looks again.</summary>
public static class MealPassSchedule
{
    /// <summary>Between passes. An hour: the deadline is settled within the hour after Sunday 8:00, and a pass with nothing due reads local files only.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>The minute past each hour the pass is aimed at, on the household's clock: after the hour has turned, so Sunday's 8:00 deadline is behind it by 8:05.</summary>
    public const int MinutePastTheHour = 5;

    /// <summary>
    /// The minute past each hour the second request is aimed at (D120): thirty minutes after the first, so together the two are a pass every half hour, and a recipe's window of
    /// an hour always holds two of them. One that is late, or that the phone skipped, does not lose the page.
    /// </summary>
    public const int RecipeMinutePastTheHour = 35;

    /// <summary>The first pass of each day that plans: six o'clock on the household's clock, before the workout run's quarter to seven.</summary>
    public static readonly TimeOnly PlanFrom = new(6, 0);

    /// <summary>How long from <paramref name="now"/> until the household's clock next reads <see cref="MinutePastTheHour"/> past an hour. Exactly on it waits the whole <see cref="Interval"/>.</summary>
    public static TimeSpan DelayUntilNext(DateTimeOffset now, TimeZoneInfo zone) => DelayUntil(now, zone, MinutePastTheHour);

    /// <summary>How long from <paramref name="now"/> until the household's clock next reads <see cref="RecipeMinutePastTheHour"/> past an hour. Exactly on it waits the whole <see cref="Interval"/>.</summary>
    public static TimeSpan DelayUntilNextRecipeLook(DateTimeOffset now, TimeZoneInfo zone) => DelayUntil(now, zone, RecipeMinutePastTheHour);

    private static TimeSpan DelayUntil(DateTimeOffset now, TimeZoneInfo zone, int minutePastTheHour)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var into = TimeSpan.FromTicks(local.TimeOfDay.Ticks % Interval.Ticks);
        var target = TimeSpan.FromMinutes(minutePastTheHour);
        return into < target ? target - into : target + Interval - into;
    }
}

/// <summary>What is due now. Pure: it reads nothing; the records and the stamp are handed in.</summary>
public static class MealPassPlan
{
    /// <summary>How long after a try that did not settle a scheduled pass waits before trying again.</summary>
    public static readonly TimeSpan RetryGap = TimeSpan.FromHours(3);

    /// <summary>Whether a run's answer is final for the day: planned, or this phone is not the planner. Anything else is something the household can mend.</summary>
    public static bool Settles(MealWeekOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome switch
        {
            MealWeekOutcome.Planned => true,
            MealWeekOutcome.NotPlanned notPlanned => notPlanned.Because == MealNotPlannedBecause.NotThePlanner,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This kind of outcome has no answer on whether it settles; add one here."),
        };
    }

    /// <summary>
    /// <paramref name="tabletPerson"/> is the adult whose tablet this phone has (null: none, so no recipe is ever due) and <paramref name="delivered"/> looks a page up in the record of
    /// what has gone to the tablet; it is called only when a recipe's hour is open, so a pass outside every hour reads nothing more than before.
    /// </summary>
    public static MealPassWork Decide(
        MealPassTrigger trigger,
        DateTimeOffset now,
        TimeZoneInfo zone,
        MealPassStampRead stamp,
        MealWeekRecordRead weeks,
        MealDecisionRecordRead decisions,
        string? tabletPerson = null,
        Func<string, DeliveredMealDocument?>? delivered = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(stamp);
        ArgumentNullException.ThrowIfNull(weeks);
        ArgumentNullException.ThrowIfNull(decisions);

        // A stamp that cannot be read is no stamp: the cost is one extra, idempotent run.
        var held = (stamp as MealPassStampRead.Held)?.Stamp ?? MealPassStamp.Empty;
        var recipes = MealRecipeHour.DueWeeks(weeks, now, tabletPerson, delivered ?? (_ => null)).Count > 0;
        return new MealPassWork(PlanDue(trigger, now, zone, held), DeadlineDue(now, zone, held, weeks, decisions), recipes);
    }

    private static bool PlanDue(MealPassTrigger trigger, DateTimeOffset now, TimeZoneInfo zone, MealPassStamp stamp)
    {
        // The week to plan is the one after the week that holds now, so a Sunday turning over makes yesterday's plan the wrong week's.
        var week = PlanningWeek.StartOf(now, zone).AddDays(PlanningWeek.DaysInWeek);
        var today = ZoneClock.DayOf(now, zone);
        var latestPlanTime = ZoneClock.At(today, MealPassSchedule.PlanFrom, zone, ClockEdge.Start);
        if (latestPlanTime > now)
        {
            latestPlanTime = ZoneClock.At(today.AddDays(-1), MealPassSchedule.PlanFrom, zone, ClockEdge.Start);
        }

        var settled = stamp.PlanSettledAt is { } at && at <= now && at >= latestPlanTime && stamp.PlanWeek == week;
        if (settled)
        {
            return false;
        }

        return trigger == MealPassTrigger.Opened || Waited(stamp.PlanTriedAt, now);
    }

    private static bool DeadlineDue(
        DateTimeOffset now, TimeZoneInfo zone, MealPassStamp stamp, MealWeekRecordRead weeks, MealDecisionRecordRead decisions)
    {
        if (!Waited(stamp.DeadlineTriedAt, now))
        {
            return false;
        }

        // A record that is there and cannot be read makes the step due: the step, not this look, says why it cannot decide.
        MealWeekRecord kept;
        switch (weeks)
        {
            case MealWeekRecordRead.NothingYet:
                return false;
            case MealWeekRecordRead.Held heldWeeks:
                kept = heldWeeks.Record;
                break;
            case MealWeekRecordRead.Unreadable:
                return true;
            default:
                throw new InvalidOperationException("The saved weeks came back as neither a record nor a reason.");
        }

        MealDecisionRecord decided;
        switch (decisions)
        {
            case MealDecisionRecordRead.NothingYet:
                decided = MealDecisionRecord.Empty;
                break;
            case MealDecisionRecordRead.Held heldDecisions:
                decided = heldDecisions.Record;
                break;
            case MealDecisionRecordRead.Unreadable:
                return true;
            default:
                throw new InvalidOperationException("The decided weeks came back as neither a record nor a reason.");
        }

        return MealDeadlineTarget.Choose(kept, decided, now, zone) is MealDeadlineChoice.Decide;
    }

    // A try waits out the gap; a try dated after now is a clock set back and waits for nothing.
    private static bool Waited(DateTimeOffset? tried, DateTimeOffset now) =>
        tried is not { } at || at > now || now - at >= RetryGap;
}

/// <summary>Which part of the pass a trail line is about.</summary>
public enum MealPassPart
{
    Plan,
    Deadline,

    /// <summary>A recipe in its hour (D120).</summary>
    Recipes,
}

/// <summary>What went wrong with a part, in the words the trail knows.</summary>
public enum MealPassProblem
{
    /// <summary>Another run held the calendars for the whole wait; this one never started.</summary>
    Busy,

    /// <summary>The part threw; the line carries only the exception's type name.</summary>
    Failed,

    /// <summary>The pass could not write its own stamp; the work itself had finished.</summary>
    StampNotSaved,
}

/// <summary>The pass's own words in the Body trail: fixed phrases and an exception type name, nothing the household wrote. The runs it calls write their own lines.</summary>
public static class MealPassTrail
{
    public static string LogPhrase(MealPassPart part, MealPassProblem problem, string failureType)
    {
        if (!Enum.IsDefined(part))
        {
            throw new ArgumentOutOfRangeException(nameof(part), part, "There is no such part of the pass.");
        }

        switch (problem)
        {
            case MealPassProblem.Busy:
                if (part == MealPassPart.Plan)
                {
                    return "meal week busy, try again later";
                }

                if (part == MealPassPart.Recipes)
                {
                    return "meal recipes busy, try again later";
                }

                return "meal deadline busy, try again later";
            case MealPassProblem.Failed:
                if (part == MealPassPart.Plan)
                {
                    return $"meal week failed: {failureType}";
                }

                if (part == MealPassPart.Recipes)
                {
                    return $"meal recipes failed: {failureType}";
                }

                return $"meal deadline failed: {failureType}";
            case MealPassProblem.StampNotSaved:
                return $"meal pass stamp not saved: {failureType}";
            default:
                throw new ArgumentOutOfRangeException(nameof(problem), problem, "This problem has no trail phrase; add one here.");
        }
    }
}
