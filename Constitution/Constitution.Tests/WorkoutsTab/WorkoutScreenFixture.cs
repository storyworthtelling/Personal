using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// The invented fortnight the Workouts tab tests stand on. Today is Tuesday 3 November 2026 at 7 am (standard time), so the
/// window runs Wednesday 4th to Tuesday 10th across two plan weeks. Sam's week: Tue strength, Wed mobility, Thu strength,
/// Fri rest, Sat treadmill, Sun a gentle walk; the next Monday strength and Tuesday mobility. Nothing in it breaks a rule.
/// </summary>
internal static class WorkoutScreenFixture
{
    public static readonly DateOnly Tue = new(2026, 11, 3);
    public static readonly DateOnly Wed = new(2026, 11, 4);
    public static readonly DateOnly Thu = new(2026, 11, 5);
    public static readonly DateOnly Fri = new(2026, 11, 6);
    public static readonly DateOnly Sat = new(2026, 11, 7);
    public static readonly DateOnly Sun = new(2026, 11, 8);
    public static readonly DateOnly Mon = new(2026, 11, 9);
    public static readonly DateOnly NextTue = new(2026, 11, 10);

    public static DateTimeOffset Now => Est(11, 3, 7);

    public static Session Strength() => new(
        SessionKind.Strength,
        TimeSpan.FromMinutes(40),
        false,
        [
            new("Squat holding a weight", EquipmentCatalogue.DumbbellsId),
            new("Push-ups on a bench", EquipmentCatalogue.BenchId),
            new("Band row", EquipmentCatalogue.BandsId),
            new("Hip hinge"),
            new("Sit to stand"),
        ]);

    public static Session Mobility() => new(SessionKind.Mobility, TimeSpan.FromMinutes(20), false, [new("Neck and shoulder rolls"), new("Calf stretch")]);

    public static Session Treadmill() => new(
        SessionKind.Treadmill,
        TimeSpan.FromMinutes(30),
        true,
        [new("Warm-up walk", EquipmentCatalogue.TreadmillId), new("Brisk walk", EquipmentCatalogue.TreadmillId)]);

    public static Session Walk() => new(SessionKind.GentleWalk, TimeSpan.FromMinutes(45), false, [new("Easy walk")]);

    public static PlannedSession Planned(DateOnly day, int hour, int minute, Session session, (int Hour, int Minute) gapFrom, (int Hour, int Minute) gapTo, Together together = Together.None, IReadOnlyList<string>? who = null)
    {
        DateTimeOffset At(int h, int m) => Est(day.Month, day.Day, h, m);
        return new PlannedSession(day, At(hour, minute), new FreeGap(At(gapFrom.Hour, gapFrom.Minute), At(gapTo.Hour, gapTo.Minute)), session, who ?? [Sam], together);
    }

    public static PlannedSession TueStrength() => Planned(Tue, 16, 30, Strength(), (16, 0), (18, 0));

    public static PlannedSession WedMobility() => Planned(Wed, 6, 30, Mobility(), (6, 30), (8, 0));

    public static PlannedSession ThuStrength() => Planned(Thu, 16, 0, Strength(), (16, 0), (18, 0));

    public static PlannedSession SatTreadmill() => Planned(Sat, 8, 0, Treadmill(), (6, 30), (18, 0));

    public static PlannedSession SunWalk(Together together = Together.None) =>
        Planned(Sun, 14, 0, Walk(), (6, 30), (18, 0), together, together == Together.None ? null : [Sam, Lee]);

    public static PlannedSession MonStrength() => Planned(Mon, 16, 0, Strength(), (16, 0), (18, 0));

    public static PlannedSession NextTueMobility() => Planned(NextTue, 6, 30, Mobility(), (6, 30), (8, 0));

    public static IReadOnlyDictionary<DateOnly, DayFacts> Facts() => new Dictionary<DateOnly, DayFacts>
    {
        [Tue] = new(132, null, new PlacedTime(Est(11, 3, 16, 30), Est(11, 3, 17, 10)), true),
        [Wed] = new(118),
        [Thu] = new(132, null, new PlacedTime(Est(11, 5, 16, 0), Est(11, 5, 16, 40))),
        [Sat] = new(140, "Moved indoors: rain forecast", new PlacedTime(Est(11, 7, 8, 0), Est(11, 7, 8, 30))),
        [Mon] = new(132),
        [NextTue] = new(118),
    };

    /// <summary>The snapshot. <paramref name="sundayTogether"/> makes the Sunday walk a family one; <paramref name="calendars"/> are Sam's days.</summary>
    public static WorkoutsSnapshot Snapshot(
        HouseholdCalendarSnapshot? calendars = null,
        Together sundayTogether = Together.None,
        EquipmentSet? equipment = null,
        SkipLedger? skips = null,
        bool nextWeek = true)
    {
        var shared = calendars ?? Calendars();
        var week0 = new WorkoutWeek(
            WorkoutFixture.Request(shared, equipment, now: Now, bothAdults: false),
            new HouseholdWorkoutPlan(
                WorkoutFixture.WeekStart,
                [new WeeklyPlan(Sam, WorkoutFixture.WeekStart, [TueStrength(), WedMobility(), ThuStrength(), SatTreadmill(), SunWalk(sundayTogether)], null)],
                []));
        var weeks = new List<WorkoutWeek> { week0 };
        if (nextWeek)
        {
            var start = WorkoutFixture.WeekStart.AddDays(7);
            weeks.Add(new WorkoutWeek(
                WorkoutFixture.Request(shared, equipment, now: Now, bothAdults: false, weekShift: 7),
                new HouseholdWorkoutPlan(start, [new WeeklyPlan(Sam, start, [MonStrength(), NextTueMobility()], null)], [])));
        }

        return new WorkoutsSnapshot(Sam, Tue, Now, weeks, Facts(), skips ?? SkipLedger.Empty);
    }

    /// <summary>Everything on the day is spoken for except <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static HouseholdCalendarSnapshot OnlyFreeOn(DateOnly day, (int Hour, int Minute) from, (int Hour, int Minute) to) =>
        Calendars(sam: WorkoutFixture.OnlyFree(day.DayNumber - WorkoutFixture.WeekStart.DayNumber, from, to));
}
