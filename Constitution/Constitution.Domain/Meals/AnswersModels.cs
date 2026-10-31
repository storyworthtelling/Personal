using Constitution.Domain.Calendar;
using Constitution.Domain.Household;

namespace Constitution.Domain.Meals;

/// <summary>What one adult's tablet said about one meal-sheet line, once its ink was read.</summary>
public enum MealVerdict
{
    /// <summary>A tick: yes.</summary>
    Approved,

    /// <summary>A written reason, or a mark in the "No, because" field.</summary>
    Denied,

    /// <summary>Untouched: unchanged until the deadline says what silence means.</summary>
    Unchanged,
}

/// <summary>
/// One adult's answer to one line of one week's meal sheet (C-2.4.5), as the install that reads their tablet saw it. Both
/// adults get the same sheet (D85: each phone pairs to its own reMarkable), so the planner's install can only learn the
/// second adult's answers if the other install writes them down; this is what it writes.
///
/// <para><paramref name="ItemId"/> is the line's YES box id (<see cref="Documents.MealSheetIds"/>), frozen and lower-case. <see cref="ReadAt"/>
/// is when this install read the ink: it, not the clock of the decision, says whether an answer was in before the deadline,
/// because the same ink read again later is the same answer, not a later one. A reason belongs only to a denial, and is the
/// adult's own handwriting, kept as it was read.</para>
/// </summary>
public sealed record MealAnswer
{
    public MealAnswer(PersonId person, DateOnly week, string itemId, MealVerdict verdict, string? reason, DateTimeOffset readAt)
    {
        if (person.Value is null)
        {
            throw new ArgumentException("An answer is somebody's; there is no person here.", nameof(person));
        }

        PlanningWeek.RequireStart(week, nameof(week));
        if (string.IsNullOrWhiteSpace(itemId) || itemId != itemId.Trim() || itemId != itemId.ToLowerInvariant())
        {
            throw new ArgumentException("A line is named by its frozen id: lower-case, with no blanks around it, and not empty.", nameof(itemId));
        }

        var words = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (words is not null && verdict != MealVerdict.Denied)
        {
            throw new ArgumentException("Only a no carries reason words; a tick or silence has nothing to explain.", nameof(reason));
        }

        Person = person;
        Week = week;
        ItemId = itemId;
        Verdict = verdict;
        Reason = words;
        ReadAt = readAt;
    }

    public PersonId Person { get; }

    /// <summary>The first day of the week the sheet plans.</summary>
    public DateOnly Week { get; }

    public string ItemId { get; }

    public MealVerdict Verdict { get; }

    public string? Reason { get; }

    public DateTimeOffset ReadAt { get; }
}
