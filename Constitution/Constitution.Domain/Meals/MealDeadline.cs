using Constitution.Domain.Calendar;
using Constitution.Domain.Documents;

namespace Constitution.Domain.Meals;

/// <summary>
/// The deadline of a weekly meal sheet and what the two adults' answers come to when it passes (C-2.4.5). Pure: the instant is
/// passed in, and nothing here reads a clock.
///
/// <para><b>The rule.</b> A line is approved if either adult ticked it and nobody denied it; denied if either adult denied it,
/// with their words; and an untouched line is approved when the deadline passes. So a denial counts the moment it is read, and
/// nothing else is decided before the deadline: a tick can still be met by the other adult's no.</para>
///
/// <para><b>On time and late.</b> An answer is on time if its tablet was read before the deadline (<see cref="MealAnswer.ReadAt"/>
/// strictly earlier). The deadline sits after the morning read (<see cref="TimeOfDay"/> is later than
/// <see cref="ReadBack.ReadBackSchedule"/>), so a Saturday denial, read on Sunday morning, is on time. Once the deadline has
/// passed, the on-time answers decide the line and a <b>late answer never reverses it</b>: it is set aside
/// (<see cref="MealItemDecision.Ignored"/>) for the planner to say so. A line nobody read on time is decided by the late
/// readings instead, by the same rule, because the alternative is a week that never plans; a line nobody read at all is
/// <see cref="MealItemOutcome.Unheard"/>, never approved, because silence approves at a read and there was none.</para>
///
/// <para><b>It is a function of the set of answers.</b> The order they arrive in, and which install wrote them, change nothing;
/// two installs holding the same records give the same verdict.</para>
/// </summary>
public static class MealDeadline
{
    /// <summary>When silence starts to approve: Sunday morning on the owner's clock, after the morning read-back has run.</summary>
    public static readonly TimeOnly TimeOfDay = new(8, 0);

    /// <summary>
    /// The deadline for the sheet that plans the week opening on <paramref name="weekStart"/>: <see cref="TimeOfDay"/> on that
    /// Sunday, as the owner's clock reads it. It is wall-clock time, so across a clock change the wait from Thursday is an hour
    /// longer or shorter than seventy-two.
    /// </summary>
    public static DateTimeOffset ForWeek(DateOnly weekStart, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        PlanningWeek.RequireStart(weekStart, nameof(weekStart));
        return ZoneClock.At(weekStart, TimeOfDay, zone, ClockEdge.Start);
    }

    /// <summary>The deadline a built sheet carries.</summary>
    public static DateTimeOffset Of(MealSheetModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Deadline;
    }

    /// <summary>
    /// The verdict on every line the answers speak of, plus every line in <paramref name="expected"/> (the sheet's own lines,
    /// <see cref="MealAnswerRecords.ExpectedItems"/>) that they do not. The sheet's lines come in the sheet's order, then any others in
    /// id order. All answers must be for one week.
    /// </summary>
    public static MealDecision Decide(IEnumerable<MealAnswer> answers, DateTimeOffset deadline, DateTimeOffset now, IEnumerable<string>? expected = null)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var all = answers.ToList();
        var weeks = all.Select(answer => answer.Week).Distinct().ToList();
        if (weeks.Count > 1)
        {
            throw new ArgumentException($"A verdict is for one week's sheet; these answers are for {weeks.Count} weeks ({string.Join(", ", weeks.Order())}).", nameof(answers));
        }

        var settled = now >= deadline;

        // The sheet's own lines come first in the sheet's own order; any other line the answers speak of follows in id order.
        var asked = (expected ?? []).Distinct(StringComparer.Ordinal).ToList();
        var ids = asked.Concat(all.Select(answer => answer.ItemId).Except(asked, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var items = ids.Select(id => Judge(id, all.Where(answer => answer.ItemId == id).ToList(), deadline, settled)).ToList();
        return new MealDecision(weeks.Count == 1 ? weeks[0] : null, deadline, settled, items);
    }

    private static MealItemDecision Judge(string itemId, List<MealAnswer> answers, DateTimeOffset deadline, bool settled)
    {
        var onTime = LatestPerPerson(answers.Where(answer => answer.ReadAt < deadline));
        var late = answers.Where(answer => answer.ReadAt >= deadline).OrderBy(answer => answer.ReadAt).ThenBy(answer => answer.Person.Value, StringComparer.Ordinal).ToList();

        if (onTime.Any(answer => answer.Verdict == MealVerdict.Denied))
        {
            return Denied(itemId, onTime, late);
        }

        if (!settled)
        {
            return new MealItemDecision(itemId, MealItemOutcome.Pending, MealApprovalBasis.None, [], []);
        }

        if (onTime.Count > 0)
        {
            return Approved(itemId, onTime, late);
        }

        // D103: a line no tablet was read for by the deadline is approved like any untouched line. Late readings never decide it.
        return new MealItemDecision(itemId, MealItemOutcome.Approved, MealApprovalBasis.Silence, [], late, NothingWasRead: true);
    }

    private static MealItemDecision Denied(string itemId, List<MealAnswer> deciding, List<MealAnswer> ignored) =>
        new(
            itemId,
            MealItemOutcome.Denied,
            MealApprovalBasis.None,
            deciding.Where(answer => answer.Verdict == MealVerdict.Denied).Select(answer => new MealDenial(answer.Person.Value, answer.Reason)).ToList(),
            ignored);

    private static MealItemDecision Approved(string itemId, List<MealAnswer> deciding, List<MealAnswer> ignored) =>
        new(
            itemId,
            MealItemOutcome.Approved,
            deciding.Any(answer => answer.Verdict == MealVerdict.Approved) ? MealApprovalBasis.Tick : MealApprovalBasis.Silence,
            [],
            ignored);

    /// <summary>
    /// Each person's latest reading, people in order. A tie in time goes to the firmer word (a no, then a tick, then silence), then to
    /// the reason's text, so that two readings the same instant resolve the same way whatever order they were handed in.
    /// </summary>
    private static List<MealAnswer> LatestPerPerson(IEnumerable<MealAnswer> answers) =>
        answers
            .GroupBy(answer => answer.Person.Value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(answer => answer.ReadAt)
                .ThenBy(answer => answer.Verdict == MealVerdict.Denied ? 0 : answer.Verdict == MealVerdict.Approved ? 1 : 2)
                .ThenBy(answer => answer.Reason ?? string.Empty, StringComparer.Ordinal)
                .First())
            .ToList();
}
