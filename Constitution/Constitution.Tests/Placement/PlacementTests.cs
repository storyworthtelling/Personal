using System.Globalization;
using System.Text.RegularExpressions;
using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Tests.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Placement;

/// <summary>
/// C-5.3.2 · Placing events. The four named tests are the acceptance; the rest pin what each leaves open
/// (the boundary of a gap, what a gap that is wrong cannot do, a calendar nobody could read).
///
/// <para>Sessions are built by hand so each test controls exactly the gap it is about. On a weekday Sam's
/// free afternoon is 4 to 6 pm (work ends at four, dinner is six to seven); Lee's starts at 3:30.</para>
/// </summary>
public class PlacementTests
{
    // ---- acceptance 1: household events are placed before workouts ----

    [Fact]
    public void HouseholdEventsFirst()
    {
        // Sam is free on Tuesday only from 4 to 5 pm: work ends at 4 and the dentist is at 5.
        var calendars = Calendars(sam: [CalendarEntry.Timed("Dentist", At(1, 17), At(1, 18))]);
        var workout = Planned(1, (16, 0), (17, 0), Effort(40, sweaty: false), Sam);
        var cook = Cook(Sam, 1, (16, 0), (17, 0), 45);

        var plan = PlaceWeek(calendars, PlanOf(workout), cook);

        var dinner = Assert.Single(plan.ToCreate);
        Assert.Equal("Cook: chili", dinner.Title);
        Assert.Equal(At(1, 16), dinner.Start);
        Assert.Equal(At(1, 16, 45), dinner.End);
        var refused = Assert.Single(plan.Unplaced);
        Assert.Equal("Workout · 40 min", refused.Title);
        Assert.Equal(UnplacedReason.NoRoom, refused.Reason);

        // Without the household event the same workout takes the hour, so the cook is what displaced it.
        var alone = Assert.Single(PlaceWeek(calendars, PlanOf(workout)).ToCreate);
        Assert.Equal(At(1, 16), alone.Start);
        Assert.Equal(At(1, 16, 40), alone.End);
    }

    [Fact]
    public void WorkoutsGoAroundHouseholdEvents()
    {
        var calendars = Calendars();
        var workout = Planned(1, (16, 0), (18, 0), Effort(40, sweaty: false), Sam);
        var cook = Cook(Sam, 1, (16, 30), (17, 30), 45);

        var plan = PlaceWeek(calendars, PlanOf(workout), cook);

        var dinner = Made(plan, "Cook: chili", Sam);
        Assert.Equal(At(1, 16, 30), dinner.Start);
        Assert.Equal(At(1, 17, 15), dinner.End);
        // 4:00 to 4:30 is too short for forty minutes, so the workout takes the first stretch that is not.
        var exercise = Made(plan, "Workout · 40 min", Sam);
        Assert.Equal(At(1, 17, 15), exercise.Start);
        Assert.Equal(At(1, 17, 55), exercise.End);
        Assert.Empty(plan.Unplaced);
    }

    [Fact]
    public void HouseholdEventsClaimTimeInTheOrderGiven()
    {
        var calendars = Calendars();
        var first = Household("shop-a", "Groceries", 60, 1, (16, 0), (17, 0), Sam);
        var second = Household("dinner-a", "Cook: chili", 30, 1, (16, 0), (17, 0), Sam);

        var plan = PlaceWeek(calendars, null, first, second);

        Assert.Equal(At(1, 16), Made(plan, "Groceries", Sam).Start);
        var refused = Assert.Single(plan.Unplaced);
        Assert.Equal("Cook: chili", refused.Title);
        Assert.Equal(UnplacedReason.NoRoom, refused.Reason);
    }

    // ---- acceptance 2: a sweaty session leaves room to shower ----

    [Fact]
    public void ShowerBufferAfterSweatySessions()
    {
        var calendars = Calendars();
        var sweaty = Planned(1, (16, 0), (18, 0), Effort(40, sweaty: true), Sam);
        var gentle = PlannedAt(1, (16, 0), (18, 0), (16, 30), Effort(20, sweaty: false, SessionKind.Mobility), Together.None, Sam);

        var plan = PlaceWeek(calendars, PlanOf(sweaty, gentle));

        var first = Made(plan, "Workout · 40 min", Sam);
        Assert.Equal(At(1, 16), first.Start);
        Assert.Equal(At(1, 16, 40), first.End);
        // The next thing starts a shower later: 4:40 plus fifteen minutes.
        var second = Made(plan, "Workout · 20 min", Sam);
        Assert.Equal(At(1, 16, 55), second.Start);
        Assert.Equal(first.End + ShowerBuffer, second.Start);
        Assert.Equal(At(1, 17, 15), second.End);
        // The shower is room, not an event: the calendar is asked for two events and no third.
        Assert.Equal(2, plan.ToCreate.Count);
    }

    [Fact]
    public void ASessionThatIsNotSweatyNeedsNoShower()
    {
        var calendars = Calendars();
        var first = Planned(1, (16, 0), (18, 0), Effort(40, sweaty: false), Sam);
        var second = PlannedAt(1, (16, 0), (18, 0), (16, 30), Effort(20, sweaty: false, SessionKind.Mobility), Together.None, Sam);

        var plan = PlaceWeek(calendars, PlanOf(first, second));

        Assert.Equal(At(1, 16, 40), Made(plan, "Workout · 20 min", Sam).Start);
    }

    [Fact]
    public void TheShowerMustFitInsideTheGapToo()
    {
        var calendars = Calendars();
        // Forty minutes plus the fifteen is 55: a gap of 55 holds it exactly, a gap of 54 does not.
        var exact = PlaceWeek(calendars, PlanOf(Planned(1, (16, 0), (16, 55), Effort(40), Sam)));
        var short54 = PlaceWeek(calendars, PlanOf(Planned(1, (16, 0), (16, 54), Effort(40), Sam)));

        var held = Assert.Single(exact.ToCreate);
        Assert.Equal(At(1, 16), held.Start);
        Assert.Equal(At(1, 16, 40), held.End);
        Assert.Empty(short54.ToCreate);
        var refused = Assert.Single(short54.Unplaced);
        Assert.Equal(UnplacedReason.NoRoomForShowerBuffer, refused.Reason);
    }

    [Fact]
    public void TheShowerNeverCrossesBusyTime()
    {
        // The gap the planner handed over runs to 5 pm, but Sam's dentist is at 4:45: the session fits the
        // hour, the shower does not, and the shower is not allowed to sit on top of the dentist.
        var calendars = Calendars(sam: [CalendarEntry.Timed("Dentist", At(1, 16, 45), At(1, 18))]);

        var plan = PlaceWeek(calendars, PlanOf(Planned(1, (16, 0), (17, 0), Effort(40), Sam)));

        Assert.Empty(plan.ToCreate);
        Assert.Equal(UnplacedReason.NoRoomForShowerBuffer, Assert.Single(plan.Unplaced).Reason);
    }

    [Fact]
    public void ASessionBiggerThanItsGapIsRefused()
    {
        var calendars = Calendars();

        var plain = PlaceWeek(calendars, PlanOf(Planned(1, (16, 0), (16, 30), Effort(40, sweaty: false), Sam)));
        var sweaty = PlaceWeek(calendars, PlanOf(Planned(1, (16, 0), (16, 30), Effort(40, sweaty: true), Sam)));

        Assert.Empty(plain.ToCreate);
        Assert.Equal(UnplacedReason.NoRoom, Assert.Single(plain.Unplaced).Reason);
        Assert.Empty(sweaty.ToCreate);
        Assert.Equal(UnplacedReason.NoRoom, Assert.Single(sweaty.Unplaced).Reason);
    }

    [Fact]
    public void ADefaultShowerIsFifteenMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), new PlacementOptions().ShowerBuffer);
    }

    // ---- what placement is not allowed to do ----

    [Fact]
    public void ThePlannedGapIsNeverLeft()
    {
        // Sam is free until six, but the gap the planner handed over ends at five. With the first forty
        // minutes taken by the cook, the 20 minutes left inside the gap cannot hold a 30-minute session,
        // and the free hour beyond the gap is not Placement's to use.
        var calendars = Calendars();
        var cook = Cook(Sam, 1, (16, 0), (16, 40), 40);
        var walk = Planned(1, (16, 0), (17, 0), Effort(30, sweaty: false, SessionKind.Mobility), Sam);

        var plan = PlaceWeek(calendars, PlanOf(walk), cook);

        Assert.Single(plan.ToCreate);
        Assert.Equal(UnplacedReason.NoRoom, Assert.Single(plan.Unplaced).Reason);
    }

    [Fact]
    public void BusyTimeIsTheLastWord()
    {
        // A gap that wrongly starts at three, an hour inside Sam's workday: the session still starts at four.
        var calendars = Calendars();
        var request = PlaceRequest(calendars, PlanOf(Planned(1, (15, 0), (17, 0), Effort(40), Sam)));

        var plan = PlacementEngine.Plan(request, Fresh);

        var made = Assert.Single(plan.ToCreate);
        Assert.Equal(At(1, 16), made.Start);
        Assert.True(request.People.Single(person => person.Person == Sam).IsFree(made.Start, made.End));
    }

    [Fact]
    public void ASessionNeverStartsBeforeItsGap()
    {
        var calendars = Calendars();
        var plan = PlaceWeek(calendars, PlanOf(PlannedAt(1, (16, 30), (18, 0), (16, 0), Effort(30, false, SessionKind.Mobility), Together.None, Sam)));

        Assert.Equal(At(1, 16, 30), Assert.Single(plan.ToCreate).Start);
    }

    [Fact]
    public void AProposalThatFitsIsKept()
    {
        // The planner proposes 5:00 inside a 4-to-6 gap; nothing is in the way, so 5:00 stays.
        var calendars = Calendars();
        var plan = PlaceWeek(calendars, PlanOf(PlannedAt(1, (16, 0), (18, 0), (17, 0), Effort(30, false, SessionKind.Mobility), Together.None, Sam)));

        Assert.Equal(At(1, 17), Assert.Single(plan.ToCreate).Start);
    }

    [Fact]
    public void AHouseholdEventNeedsEveryoneFree()
    {
        // Lee is at a parents' evening until 4:30; Sam is free from 4. Together they are free from 4:30.
        var calendars = Calendars(lee: [CalendarEntry.Timed("Parent evening", At(1, 15, 30), At(1, 16, 30))]);
        var shopping = Household("shop-a", "Groceries", 30, 1, (16, 0), (17, 30), Sam, Lee);

        var plan = PlaceWeek(calendars, null, shopping);

        var sam = Made(plan, "Groceries", Sam);
        var lee = Made(plan, "Groceries", Lee);
        Assert.Equal(At(1, 16, 30), sam.Start);
        Assert.Equal(At(1, 17), sam.End);
        Assert.Equal(sam.Start, lee.Start);
        Assert.Equal(sam.End, lee.End);
        Assert.NotEqual(sam.Key, lee.Key);
    }

    [Fact]
    public void TheBusierOfTwoDecidesWhoeverTheyAre()
    {
        // The same errand, but now it is Sam who is held up until half past four and Lee who is free from half past three.
        var calendars = Calendars(sam: [CalendarEntry.Timed("Dentist", At(1, 16), At(1, 16, 30))]);
        var shopping = Household("shop-a", "Groceries", 30, 1, (15, 30), (17, 30), Sam, Lee);

        var plan = PlaceWeek(calendars, null, shopping);

        Assert.Equal(At(1, 16, 30), Made(plan, "Groceries", Sam).Start);
        Assert.Equal(At(1, 16, 30), Made(plan, "Groceries", Lee).Start);
    }

    [Fact]
    public void ASharedSessionAlsoWaitsForSam()
    {
        var calendars = Calendars(sam: [CalendarEntry.Timed("Coffee", At(6, 10), At(6, 10, 20))]);
        var walk = PlannedAt(6, (10, 0), (11, 0), (10, 0), Effort(40, false, SessionKind.GentleWalk), Together.FamilyWalk, Sam, Lee);

        var plan = PlaceWeek(calendars, PlanOf(walk));

        Assert.Equal(At(6, 10, 20), Made(plan, "Workout · 40 min", Sam).Start);
        Assert.Equal(At(6, 10, 20), Made(plan, "Workout · 40 min", Lee).Start);
    }

    [Fact]
    public void TimeNobodyReadIsNotFree()
    {
        // The busy time was read for the week of the second; Monday the ninth was never read, so it is not free.
        var calendars = Calendars();
        var nextMonday = Household("shop-a", "Groceries", 30, 7, (16, 0), (17, 0), Sam);

        var plan = PlaceWeek(calendars, null, nextMonday);

        Assert.Empty(plan.ToCreate);
        Assert.Equal(UnplacedReason.NoRoom, Assert.Single(plan.Unplaced).Reason);
    }

    [Fact]
    public void TwoEventsUnderOneKeyAreOneEvent()
    {
        // A write that was retried can leave two events with one key. They are one session: the earlier id's
        // time stands, and neither is touched.
        var request = PlaceRequest(Calendars(), PlanOf(Planned(1, (16, 0), (18, 0), Effort(40), Sam)));
        var key = StrengthKey(Sam);
        var state = new CalendarState(
            WrittenKeys.Empty.With(key),
            [new HeldEvent("b", key, At(1, 17), At(1, 17, 40)), new HeldEvent("a", key, At(1, 16), At(1, 16, 40))],
            new HashSet<string>());

        var plan = PlacementEngine.Plan(request, state);

        var held = Assert.IsType<EventHeld>(Assert.Single(plan.Outcomes));
        Assert.Equal(At(1, 16), held.Start);
    }

    [Fact]
    public void AnEventTriesEachWindowInOrder()
    {
        var calendars = Calendars();
        // The first window is Sam's workday; the second is free.
        var event1 = new HouseholdEventRequest(
            "shop-a",
            "Groceries",
            TimeSpan.FromMinutes(30),
            [Sam],
            [new PlacementWindow(At(1, 10), At(1, 11)), new PlacementWindow(At(1, 16, 30), At(1, 17, 30))]);

        var plan = PlaceWeek(calendars, null, event1);

        Assert.Equal(At(1, 16, 30), Assert.Single(plan.ToCreate).Start);
    }

    [Fact]
    public void ASharedSessionIsPlacedOnceForBoth()
    {
        // The family walk is one session; Sam and Lee each get it on their own calendar at the same time.
        var calendars = Calendars();
        var walk = PlannedAt(6, (10, 0), (11, 0), (10, 0), Effort(40, false, SessionKind.GentleWalk), Together.FamilyWalk, Sam, Lee);

        var plan = PlaceWeek(calendars, PlanOf(walk));

        var sam = Made(plan, "Workout · 40 min", Sam);
        var lee = Made(plan, "Workout · 40 min", Lee);
        Assert.Equal(At(6, 10), sam.Start);
        Assert.Equal(sam.Start, lee.Start);
        Assert.Equal(sam.End, lee.End);
        Assert.NotEqual(sam.Key, lee.Key);
        Assert.Equal(2, plan.ToCreate.Count);
    }

    [Fact]
    public void ASharedSessionWaitsForBothCalendars()
    {
        var calendars = Calendars(lee: [CalendarEntry.Timed("Coffee", At(6, 10), At(6, 10, 20))]);
        var walk = PlannedAt(6, (10, 0), (11, 0), (10, 0), Effort(40, false, SessionKind.GentleWalk), Together.FamilyWalk, Sam, Lee);

        var plan = PlaceWeek(calendars, PlanOf(walk));

        Assert.Equal(At(6, 10, 20), Made(plan, "Workout · 40 min", Sam).Start);
        Assert.Equal(At(6, 10, 20), Made(plan, "Workout · 40 min", Lee).Start);
    }

    [Fact]
    public void AnUnreadableCalendarPlacesNothingForItsOwner()
    {
        // Lee has not shared her calendar: nothing is placed on her, and nothing is placed on a guess.
        var calendars = CalendarsWith(lee: CalendarRead.Unreadable("Lee has not shared her calendar yet."));
        var soloLee = Planned(1, (16, 0), (18, 0), Effort(40), Lee);
        var soloSam = Planned(2, (16, 0), (18, 0), Effort(40), Sam);
        var walk = PlannedAt(6, (10, 0), (11, 0), (10, 0), Effort(40, false, SessionKind.GentleWalk), Together.FamilyWalk, Sam, Lee);
        var shopping = Household("shop-a", "Groceries", 30, 1, (16, 0), (17, 30), Lee);

        var plan = PlaceWeek(calendars, PlanOf(soloLee, soloSam, walk), shopping);

        var placed = Assert.Single(plan.ToCreate);
        Assert.Equal(Sam, placed.Person);
        Assert.Equal(At(2, 16), placed.Start);
        Assert.Equal(4, plan.Unplaced.Count);
        Assert.All(plan.Unplaced, item => Assert.Equal(UnplacedReason.CalendarUnreadable, item.Reason));
        // The shared walk waits for Lee as a whole: Sam's copy is not placed on a guess about her morning.
        Assert.Contains(plan.Unplaced, item => item.Person == Sam && item.Title == "Workout · 40 min");
        Assert.Contains(plan.Unplaced, item => item.Person == Lee && item.Title == "Groceries");
    }

    // ---- acceptance 3: the key never includes the date ----

    [Fact]
    public void TheKeyNeverIncludesTheDate()
    {
        var calendars = Calendars();
        var plan = PlaceWeek(
            calendars,
            PlanOf(
                Planned(1, (16, 0), (18, 0), Effort(40), Sam),
                Planned(3, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Sam),
                Planned(5, (16, 0), (18, 0), Effort(40), Lee)),
            Household("shop-a", "Groceries", 30, 6, (9, 0), (12, 0), Sam, Lee));
        Assert.True(plan.ToCreate.Count >= 5, "The plan placed too little to say anything about its keys.");

        var dates = Enumerable.Range(0, 800).Select(offset => new DateOnly(2026, 1, 1).AddDays(offset)).ToList();
        string[] formats = ["yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd", "MM/dd/yyyy", "dd.MM.yyyy", "yyyy-MM"];
        foreach (var outcome in plan.Outcomes)
        {
            var key = outcome.Key.Value;
            Assert.NotEmpty(key);
            foreach (var date in dates)
            {
                foreach (var format in formats)
                {
                    Assert.DoesNotContain(date.ToString(format, CultureInfo.InvariantCulture), key, StringComparison.Ordinal);
                }
            }

            Assert.DoesNotMatch(@"\d{4}[-/.]\d{1,2}[-/.]\d{1,2}", key);
            Assert.DoesNotMatch(@"\d{8}", key);
            Assert.DoesNotMatch(
                "(?i)january|february|march|april|june|july|august|september|october|november|december|monday|tuesday|wednesday|thursday|friday|saturday|sunday",
                key);
        }
    }

    [Fact]
    public void ASessionKeepsItsKeyWhereverItLands()
    {
        // The same session proposed on a Monday or on a Wednesday is the same event: that is how a
        // moved event is found again.
        var calendars = Calendars();
        var monday = PlaceWeek(calendars, PlanOf(Planned(0, (16, 0), (18, 0), Effort(40), Sam)));
        var wednesday = PlaceWeek(calendars, PlanOf(Planned(2, (16, 30), (18, 0), Effort(40), Sam)));

        var onMonday = Assert.Single(monday.ToCreate);
        var onWednesday = Assert.Single(wednesday.ToCreate);
        Assert.NotEqual(onMonday.Start, onWednesday.Start);
        Assert.Equal(onMonday.Key, onWednesday.Key);
        Assert.Equal(StrengthKey(Sam), onMonday.Key);
    }

    [Fact]
    public void TwoSessionsOfOneKindHaveTwoKeys()
    {
        var calendars = Calendars();

        var plan = PlaceWeek(calendars, PlanOf(Planned(0, (16, 0), (18, 0), Effort(40), Sam), Planned(2, (16, 0), (18, 0), Effort(40), Sam)));

        var keys = plan.ToCreate.OrderBy(item => item.Start).Select(item => item.Key).ToList();
        Assert.Equal([StrengthKey(Sam, 0), StrengthKey(Sam, 1)], keys);
    }

    // ---- acceptance 4: the title is clean ----

    [Fact]
    public void TitlesCarryNoKey()
    {
        var calendars = Calendars();
        var plan = PlaceWeek(
            calendars,
            PlanOf(Planned(1, (16, 0), (18, 0), Effort(40), Sam), Planned(2, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Lee)),
            Cook(Lee, 3, (16, 0), (17, 30), 45),
            Household("shop-a", "Groceries", 30, 6, (9, 0), (12, 0), Sam, Lee));
        Assert.True(plan.ToCreate.Count >= 5, "The plan placed too little to say anything about its titles.");

        foreach (var outcome in plan.Outcomes)
        {
            Assert.DoesNotContain(outcome.Key.Value, outcome.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(PlacementKey.Property, outcome.Title, StringComparison.OrdinalIgnoreCase);
            foreach (var part in outcome.Key.Value.Split(':').Where(part => part.Length > 2))
            {
                Assert.DoesNotContain(part, outcome.Title, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AWorkoutIsTitledWorkoutAndMinutes()
    {
        var calendars = Calendars();
        var plan = PlaceWeek(
            calendars,
            PlanOf(
                Planned(1, (16, 0), (18, 0), Effort(40), Sam),
                Planned(2, (16, 0), (18, 0), Effort(35, false, SessionKind.OutdoorCardio), Sam),
                Planned(3, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Sam)));

        Assert.Equal(
            ["Workout · 40 min", "Workout · 35 min", "Workout · 20 min"],
            plan.ToCreate.OrderBy(item => item.Start).Select(item => item.Title));
    }

    [Fact]
    public void AHouseholdTitleIsKeptAsGiven()
    {
        var calendars = Calendars();
        var plan = PlaceWeek(
            calendars,
            null,
            Cook(Lee, 3, (16, 0), (17, 30), 45),
            Household("shop-a", "  Groceries ", 30, 6, (9, 0), (12, 0), Sam),
            Household("cooker-a", "Start the slow cooker", 10, 5, (7, 0), (7, 30), Lee));

        Assert.Equal(
            ["Cook: chili", "Groceries", "Start the slow cooker"],
            plan.ToCreate.Select(item => item.Title).Order());
    }

    [Fact]
    public void ATitleHoldingItsOwnKeyIsRefused()
    {
        var calendars = Calendars();
        var key = HouseholdKey(Lee, "dinner-a");
        var leaky = Household("dinner-a", "Cook " + key.Value, 45, 3, (16, 0), (17, 30), Lee);
        var named = Household("dinner-a", "Cook (" + PlacementKey.Property + ")", 45, 3, (16, 0), (17, 30), Lee);

        Assert.Throws<ArgumentException>(() => PlaceWeek(calendars, null, leaky));
        Assert.Throws<ArgumentException>(() => PlaceWeek(calendars, null, named));
    }

    // ---- the whole thing, on the real planner's week ----

    [Fact]
    public void ARealPlannedWeekPlacesCleanly()
    {
        var calendars = Calendars(
            sam: [CalendarEntry.Timed("Dentist", At(0, 16), At(0, 17)), .. OnlyFree(2, (16, 30), (18, 0))],
            lee: [CalendarEntry.Timed("Parent evening", At(0, 16), At(0, 18)), .. BlockDay(3)],
            family: [CalendarEntry.Timed("Piano", At(1, 16), At(1, 17, 30))]);
        var planned = WorkoutPlanner.Plan(WorkoutFixture.Request(calendars));
        var request = PlaceRequest(
            calendars,
            planned,
            // Not Monday: the family calendar has the piano lesson from four to half past five.
            Cook(Lee, 2, (16, 0), (17, 30), 45),
            Household("shop-a", "Groceries", 60, 6, (9, 0), (12, 0), Sam, Lee),
            Household("cooker-a", "Start the slow cooker", 10, 5, (7, 0), (8, 0), Lee));

        var plan = PlacementEngine.Plan(request, Fresh);

        var workouts = plan.ToCreate.Where(item => item.Title.StartsWith("Workout", StringComparison.Ordinal)).ToList();
        Assert.True(workouts.Count >= 3, $"Only {workouts.Count} workouts were placed; the invented week has room for more.");
        Assert.Equal(4, plan.ToCreate.Count(item => !item.Title.StartsWith("Workout", StringComparison.Ordinal)));

        var spans = new List<(string Person, DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var made in plan.ToCreate)
        {
            var busy = request.People.Single(person => person.Person == made.Person);
            Assert.True(busy.IsFree(made.Start, made.End), $"{made.Person}: '{made.Title}' at {made.Start:t} overlaps busy or never-placed time");
            var buffer = TimeSpan.Zero;
            if (made.Title.StartsWith("Workout", StringComparison.Ordinal))
            {
                var session = Assert.Single(
                    planned.PlanFor(made.Person)!.Sessions,
                    item => item.Gap.Start <= made.Start && made.End <= item.Gap.End && item.Session.Duration == made.End - made.Start);
                if (session.Session.IsSweaty)
                {
                    buffer = ShowerBuffer;
                }

                Assert.True(made.End + buffer <= session.Gap.End, $"{made.Person}: the workout and its shower leave their gap");
            }

            spans.Add((made.Person, made.Start, made.End + buffer));
        }

        foreach (var person in spans.GroupBy(span => span.Person))
        {
            var ordered = person.OrderBy(span => span.Start).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                Assert.True(ordered[index].Start >= ordered[index - 1].End, $"{person.Key}: two placed events overlap");
            }
        }
    }

    [Fact]
    public void ThePlanIsTheSameEveryTime()
    {
        var calendars = Calendars(sam: [CalendarEntry.Timed("Dentist", At(0, 16), At(0, 17))]);
        var planned = WorkoutPlanner.Plan(WorkoutFixture.Request(calendars));
        var request = PlaceRequest(calendars, planned, Cook(Lee, 1, (16, 0), (17, 30), 45));

        var first = PlacementEngine.Plan(request, Fresh);
        var again = PlacementEngine.Plan(request, Fresh);
        var reversed = PlacementEngine.Plan(request with { People = request.People.Reverse().ToList() }, Fresh);

        Assert.NotEmpty(first.Outcomes);
        Assert.Equal(first.Outcomes, again.Outcomes);
        Assert.Equal(first.Outcomes, reversed.Outcomes);
    }
}
