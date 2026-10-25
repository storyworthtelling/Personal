using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Tests.Calendar;

namespace Constitution.Tests.MealAnswers;

/// <summary>An invented week (Sunday 25 October 2026) and two adults, Sam and Lee. Every dish, reason and name here is made up.</summary>
internal static class MealAnswerData
{
    public static readonly PersonId Sam = new("sam");

    public static readonly PersonId Lee = new("lee");

    public static readonly DateOnly Week = new(2026, 10, 25);

    public const string Tuesday = "dinner-tue-yes";

    public const string Wednesday = "dinner-wed-yes";

    public const string Thursday = "dinner-thu-yes";

    /// <summary>Sunday 25 October, 8 am on the household clock (daylight time).</summary>
    public static DateTimeOffset Deadline { get; } = InventedFamily.Edt(10, 25, 8);

    public static DateTimeOffset Before(int minutes) => Deadline.AddMinutes(-minutes);

    public static DateTimeOffset After(int minutes) => Deadline.AddMinutes(minutes);

    public static MealAnswer Tick(PersonId person, string item, DateTimeOffset readAt, DateOnly? week = null) =>
        new(person, week ?? Week, item, MealVerdict.Approved, null, readAt);

    public static MealAnswer Deny(PersonId person, string item, string? reason, DateTimeOffset readAt, DateOnly? week = null) =>
        new(person, week ?? Week, item, MealVerdict.Denied, reason, readAt);

    public static MealAnswer Silent(PersonId person, string item, DateTimeOffset readAt, DateOnly? week = null) =>
        new(person, week ?? Week, item, MealVerdict.Unchanged, null, readAt);

    public static MealDecision Decide(DateTimeOffset now, params MealAnswer[] answers) =>
        MealDeadline.Decide(answers, Deadline, now);

    public static MealItemDecision Only(MealDecision decision) => Assert.Single(decision.Items);
}
