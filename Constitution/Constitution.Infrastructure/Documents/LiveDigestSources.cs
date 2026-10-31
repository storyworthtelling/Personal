using System.Globalization;
using Constitution.Domain.Body;
using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Infrastructure.Documents;

/// <summary>
/// The digest's four sources read from this phone's own stores: the saved workout week joined to the skip ledger (how each planned day ended), Body's local store,
/// and the kept meal weeks. It only reads. An unreadable file is a typed <see cref="DigestRead{T}.Unavailable"/> (the digest is not sent on a guess); a missing
/// one is an empty week. What is not here yet: the loved dish and the stretch dish's verdict (they need the household's ratings, so those lines are left off the page),
/// and each day's rating word.
/// </summary>
public sealed class LiveDigestSources(IWorkoutWeekReports reports, ILedgerStore ledger, IBodyStore body, IKeptWeeks meals) : IDigestSources
{
    public const string LedgerUnreadable = "The skip ledger could not be read, so the Saturday digest was not sent.";

    public DigestRead<DigestWorkoutWeek> Workouts(string person, DateOnly weekStart)
    {
        switch (reports.Load())
        {
            case WorkoutWeekRecordRead.Unreadable unreadable:
                return new DigestRead<DigestWorkoutWeek>.Unavailable(unreadable.Reason);
            case WorkoutWeekRecordRead.Held held when held.Week.WeekStart == weekStart:
                var read = ledger.Load();
                if (read.WholeFileUnreadable)
                {
                    return new DigestRead<DigestWorkoutWeek>.Unavailable(LedgerUnreadable);
                }

                var sessions = held.Week.Sessions
                    .Where(item => Same(item.Person, person) && item.Day >= weekStart && item.Day <= weekStart.AddDays(6))
                    .OrderBy(item => item.Day)
                    .Select(item => new DigestSession(item.Day, Words(item.Kind), item.Minutes, read.Ledger.OutcomeOf(person, item.Day) ?? SessionOutcome.Unsettled, null))
                    .ToList();
                var moved = held.Week.Reasons.Where(item => Same(item.Person, person)).OrderBy(item => item.Day).Select(item => item.Line).ToList();
                return new DigestRead<DigestWorkoutWeek>.Held(new DigestWorkoutWeek(weekStart, sessions, moved));
            default:
                return new DigestRead<DigestWorkoutWeek>.Held(new DigestWorkoutWeek(weekStart, [], []));
        }
    }

    public DigestRead<IReadOnlyList<BodyReading>> Readings(string person) =>
        new DigestRead<IReadOnlyList<BodyReading>>.Held(body.All());

    public DigestRead<DigestMealsWeek> Meals(DateOnly weekStart)
    {
        switch (meals.Load())
        {
            case MealWeekRecordRead.Unreadable unreadable:
                return new DigestRead<DigestMealsWeek>.Unavailable(unreadable.Reason);
            case MealWeekRecordRead.Held held when held.Record.For(weekStart) is { } week:
                var home = week.Week.Dinners.Count(item => item.Kind is DinnerKind.Cooked or DinnerKind.Leftovers);
                var outs = week.Week.Dinners.Count(item => item.Kind == DinnerKind.EatOut);
                var fresh = week.Dishes.Count(item => item.IsNew);
                var stretch = week.Week.Dinners.FirstOrDefault(item => item.IsStretch && item.Recipe is not null);
                var stretchTitle = stretch is null ? null : week.Dishes.FirstOrDefault(item => item.Recipe == stretch.Recipe)?.Title;
                return new DigestRead<DigestMealsWeek>.Held(new DigestMealsWeek(
                    null, stretchTitle, null, null,
                    [Count(home, "dinner", "dinners", " at home"), Count(outs, "night", "nights", " out"), Count(fresh, "new recipe", "new recipes", " tried")]));
            default:
                return new DigestRead<DigestMealsWeek>.Held(new DigestMealsWeek(null, null, null, null, []));
        }
    }

    public DigestRead<DigestNextWeek> Next(string person, DateOnly nextWeekStart)
    {
        var workouts = new List<DigestPlannedItem>();
        switch (reports.Load())
        {
            case WorkoutWeekRecordRead.Unreadable unreadable:
                return new DigestRead<DigestNextWeek>.Unavailable(unreadable.Reason);
            case WorkoutWeekRecordRead.Held held when held.Week.WeekStart == nextWeekStart:
                workouts.AddRange(held.Week.Sessions.Where(item => Same(item.Person, person)).OrderBy(item => item.Day).Select(item => new DigestPlannedItem(item.Day, Words(item.Kind))));
                break;
        }

        var dinners = new List<DigestPlannedItem>();
        switch (meals.Load())
        {
            case MealWeekRecordRead.Unreadable unreadable:
                return new DigestRead<DigestNextWeek>.Unavailable(unreadable.Reason);
            case MealWeekRecordRead.Held held when held.Record.For(nextWeekStart) is { } week:
                foreach (var dinner in week.Week.Dinners.Where(item => item.Kind == DinnerKind.Cooked && item.Recipe is not null))
                {
                    var title = week.Dishes.FirstOrDefault(item => item.Recipe == dinner.Recipe)?.Title;
                    if (title is not null)
                    {
                        dinners.Add(new DigestPlannedItem(dinner.Day, title));
                    }
                }

                break;
        }

        return new DigestRead<DigestNextWeek>.Held(new DigestNextWeek(workouts, dinners));
    }

    private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>"OutdoorCardio" as "Outdoor cardio".</summary>
    private static string Words(string kind)
    {
        var spaced = string.Concat(kind.Select((letter, index) => index > 0 && char.IsUpper(letter) ? " " + char.ToLowerInvariant(letter) : letter.ToString()));
        return spaced.Length == 0 ? kind : spaced;
    }

    private static readonly string[] Numbers = ["No", "One", "Two", "Three", "Four", "Five", "Six", "Seven"];

    private static string Count(int number, string one, string many, string tail)
    {
        var word = number < Numbers.Length ? Numbers[number] : number.ToString(CultureInfo.InvariantCulture);
        return word + " " + (number == 1 ? one : many) + tail;
    }
}
