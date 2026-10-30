using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Screens;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The saved weeks as the Meals tab and Today's dinner card read them (<see cref="IMealWeekSource"/>). Nothing saved is the honest
/// "no plan yet"; a file that is there and cannot be read is <see cref="MealWeekScreenRead.Unavailable"/> with its own sentence, never an
/// empty week. The day is the one on the clock the screen hands in; the week read is the one that holds it, else the next one planned
/// (<see cref="MealWeekSnapshots.Pick"/>), and a week that is over is not a plan.
/// </summary>
public sealed class MealWeekRecordSource(IMealWeekRecords records) : IMealWeekSource
{
    private readonly IMealWeekRecords records = records ?? throw new ArgumentNullException(nameof(records));

    public MealWeekScreenRead Read(DateTimeOffset now)
    {
        switch (records.Load())
        {
            case MealWeekRecordRead.Held held:
                return MealWeekSnapshots.Pick(held.Record, DateOnly.FromDateTime(now.DateTime)) is { } week
                    ? new MealWeekScreenRead.Ready(MealWeekSnapshots.From(week))
                    : new MealWeekScreenRead.NoPlanYet();
            case MealWeekRecordRead.Unreadable unreadable:
                return new MealWeekScreenRead.Unavailable(unreadable.Reason);
            default:
                return new MealWeekScreenRead.NoPlanYet();
        }
    }
}
