using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// The planner against a few hundred generated invented weeks. Each week is a seeded pile of random
/// busy entries on Sam's, Lee's and the family's calendars, random ticked equipment and a random
/// recent history; the seed is in every failure message so a failing week can be replayed. The rules
/// checked are the ones that must hold whatever the calendar says: a session sits in free time, inside
/// waking hours, once a day, never hard on consecutive days, only a gentle walk on Sunday, never an
/// unticked item, and a joint session is the same object in every participant's plan.
/// </summary>
public class WorkoutPlannerPropertyTests
{
    private const int Weeks = 300;

    [Fact]
    public void TheRulesHoldOverGeneratedWeeks()
    {
        var planned = 0;
        var joint = 0;
        for (var seed = 1; seed <= Weeks; seed++)
        {
            var random = new Random(seed);
            var shift = random.Next(2) * 7;
            var calendars = Calendars(Entries(random, shift), Entries(random, shift), FamilyEntries(random, shift));
            var equipment = EquipmentSet.Of(EquipmentCatalogue.All.Where(item => random.Next(2) == 0).Select(item => item.Id));
            var recovery = new RecoveryInputs { SessionsDoneLastWeek = random.Next(5), SessionsSkippedLastWeek = random.Next(4) };
            var request = Request(calendars, equipment, recovery, recovery, weekShift: shift);

            var plan = WorkoutPlanner.Plan(request);

            var where = $"seed {seed}";
            foreach (var week in plan.Plans)
            {
                var busy = request.People.Single(person => person.Busy.Person == week.Person).Busy;
                planned += week.Sessions.Count;
                Assert.Equal(week.Sessions.Count, week.Sessions.Select(item => item.Day).Distinct().Count());
                Assert.Equal(week.Sessions.OrderBy(item => item.Start).ToList(), week.Sessions.ToList());

                foreach (var session in week.Sessions)
                {
                    var day = session.Day.DayNumber - request.WeekStart.DayNumber + shift;
                    Assert.True(session.Gap.Length >= WorkoutPlanner.MinimumGap, $"{where}: short gap");
                    Assert.True(session.Start >= session.Gap.Start && session.End <= session.Gap.End, $"{where}: leaves its gap");
                    Assert.True(busy.IsFree(session.Start, session.End), $"{where}: {week.Person} overlaps busy time");
                    Assert.True(session.Start >= At(day, 6, 30) && session.End <= At(day, 21, 30), $"{where}: outside waking hours");
                    Assert.True(session.Start >= request.Now, $"{where}: in the past");
                    Assert.Contains(week.Person, session.Participants);
                    foreach (var id in session.Session.EquipmentNeeded)
                    {
                        Assert.True(equipment.Has(id), $"{where}: unticked {id}");
                    }

                    if (session.Day.DayOfWeek == DayOfWeek.Sunday)
                    {
                        Assert.Equal(SessionKind.GentleWalk, session.Session.Kind);
                    }

                    if (session.IsJoint)
                    {
                        joint++;
                        Assert.Contains(session, plan.JointSessions);
                        Assert.All(session.Participants, name => Assert.Contains(session, plan.PlanFor(name)!.Sessions));
                    }
                }

                var hard = week.Sessions.Where(item => SessionKinds.IsHard(item.Session.Kind)).Select(item => item.Day.DayNumber).Order().ToList();
                for (var i = 1; i < hard.Count; i++)
                {
                    Assert.True(hard[i] - hard[i - 1] >= 2, $"{where}: {week.Person} hard on consecutive days");
                }
            }

            foreach (var shared in plan.JointSessions)
            {
                // Replaces each participant's own session that day: it is the only one they have.
                Assert.All(shared.Participants, name => Assert.Single(plan.PlanFor(name)!.SessionsOn(shared.Day)));
            }
        }

        Assert.True(planned > 600, $"Only {planned} sessions were planned across {Weeks} weeks; the generator proves nothing.");
        Assert.True(joint > 20, $"Only {joint} joint placements across {Weeks} weeks; the joint rules were barely exercised.");
    }

    // A few busy stretches on random days of the week (offsets are in the planned week).
    private static List<CalendarEntry> Entries(Random random, int shift)
    {
        var entries = new List<CalendarEntry>();
        for (var i = random.Next(0, 9); i > 0; i--)
        {
            var day = shift + random.Next(7);
            var from = random.Next(6, 21);
            var length = random.Next(1, 5);
            entries.Add(CalendarEntry.Timed("Busy", At(day, from, random.Next(2) * 30), At(day, Math.Min(from + length, 23), 30)));
        }

        // Some whole days and some days left free only after the children are in bed.
        for (var i = random.Next(0, 3); i > 0; i--)
        {
            entries.AddRange(BlockDay(shift + random.Next(7)));
        }

        for (var i = random.Next(0, 3); i > 0; i--)
        {
            entries.AddRange(OnlyFree(shift + random.Next(7), (20, 0), (21, 30)));
        }

        return entries;
    }

    private static List<CalendarEntry> FamilyEntries(Random random, int shift) =>
        Enumerable.Range(0, random.Next(0, 3))
            .Select(index => shift + random.Next(7))
            .Select(day => CalendarEntry.Timed("Family", At(day, 9), At(day, 9 + random.Next(1, 8))))
            .ToList();
}
