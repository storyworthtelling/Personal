using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Placement;

/// <summary>
/// C-5.3.3 · The owner's edits win, silently. These run the real runner against <see cref="FakeCalendar"/>,
/// which can also play the owner: it moves and deletes events the way Google Calendar's own app does.
///
/// <para>The week: Sam's strength session on Tuesday (sweaty, forty minutes), a mobility session on Thursday,
/// and Lee's chili on Wednesday. All of it is written by the first run.</para>
/// </summary>
public class OwnerEditTests
{
    private static readonly PlannedSession Strength = Planned(1, (16, 0), (18, 0), Effort(40), Sam);
    private static readonly PlannedSession Mobility = Planned(3, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Sam);

    private static PlacementRequest TheWeek(HouseholdCalendarSnapshot? calendars = null, params HouseholdEventRequest[] more) =>
        PlaceRequest(
            calendars ?? Calendars(),
            PlanOf(Strength, Mobility),
            [Cook(Lee, 2, (16, 0), (17, 30), 45), .. more]);

    private sealed class Scenario
    {
        public FakeCalendar Calendar { get; } = new();

        public MemoryKeyStore Store { get; } = new();

        public Task<PlacementRun> RunAsync(PlacementRequest request) =>
            new PlacementRunner(Calendar, Store).RunAsync(request, CancellationToken.None);

        public async Task<PlacementRun> FirstRunAsync()
        {
            var run = await RunAsync(TheWeek());
            Assert.Equal(3, run.Created.Count);
            return run;
        }
    }

    // ---- a move is the new time ----

    [Fact]
    public async Task AMoveIsTheNewTime()
    {
        var week = new Scenario();
        await week.FirstRunAsync();
        var key = StrengthKey(Sam);

        // The owner drags Tuesday's workout to Thursday at half past four.
        week.Calendar.OwnerMoves(key, Sam, At(3, 16, 30), At(3, 17, 10));
        var before = week.Calendar.All.ToList();
        var createsBefore = week.Calendar.CreateCalls;

        // The next run also wants an evening errand on Thursday, which only fits if the plan has taken the
        // new time: the workout, its shower, and then thirty-five minutes to six.
        var errand = Household("shop-a", "Groceries", 35, 3, (16, 0), (18, 0), Sam);
        var run = await week.RunAsync(TheWeek(null, errand));

        var held = Assert.IsType<EventHeld>(run.Plan!.For(key));
        Assert.Equal(At(3, 16, 30), held.Start);
        Assert.Equal(At(3, 17, 10), held.End);
        var made = Assert.Single(run.Created);
        Assert.Equal("Groceries", made.Title);
        Assert.Equal(At(3, 17, 25), made.Start);
        Assert.Equal(At(3, 18), made.End);

        // And it is never moved back: the only write was the new errand.
        Assert.Equal(createsBefore + 1, week.Calendar.CreateCalls);
        Assert.Equal(At(3, 16, 30), week.Calendar.By(key, Sam)!.Start);
        Assert.All(before, item => Assert.Contains(item, week.Calendar.All));
    }

    [Fact]
    public async Task AMoveFarAwayIsStillTheSession()
    {
        var week = new Scenario();
        await week.FirstRunAsync();
        var key = StrengthKey(Sam);
        week.Calendar.OwnerMoves(key, Sam, At(8, 7), At(8, 7, 40));

        var run = await week.RunAsync(TheWeek());

        Assert.Equal(At(8, 7), Assert.IsType<EventHeld>(run.Plan!.For(key)).Start);
        Assert.Empty(run.Created);
    }

    [Fact]
    public async Task AMoveOntoAMeetingStands()
    {
        // The owner put it there. The planner keeps clear of busy time when it places; it does not police
        // where the owner chooses to stand.
        var week = new Scenario();
        await week.FirstRunAsync();
        var key = StrengthKey(Sam);
        week.Calendar.OwnerMoves(key, Sam, At(3, 17), At(3, 17, 40));
        var meeting = Calendars(sam: [CalendarEntry.Timed("Dentist", At(3, 17), At(3, 18))]);

        var run = await week.RunAsync(TheWeek(meeting));

        Assert.Equal(At(3, 17), Assert.IsType<EventHeld>(run.Plan!.For(key)).Start);
        Assert.Empty(run.Created);
        Assert.Empty(run.Plan.Unplaced);
        Assert.Equal(At(3, 17), week.Calendar.By(key, Sam)!.Start);
    }

    // ---- a delete is a skip ----

    [Fact]
    public async Task ADeleteIsASkip()
    {
        var week = new Scenario();
        await week.FirstRunAsync();
        var key = MobilityKey(Sam);
        week.Calendar.OwnerDeletes(key, Sam);
        var createsBefore = week.Calendar.CreateCalls;

        var run = await week.RunAsync(TheWeek());

        var skipped = Assert.Single(run.Plan!.Skipped);
        Assert.Equal(key, skipped.Key);
        Assert.Equal(Sam, skipped.Person);
        Assert.Empty(run.Created);
        Assert.Empty(run.Plan.Unplaced);
        Assert.Equal(createsBefore, week.Calendar.CreateCalls);
        Assert.Null(week.Calendar.By(key, Sam));
        // Only that session: the other two are still where they were put.
        Assert.Equal(2, run.Plan.Held.Count);
    }

    [Fact]
    public async Task NeverReplaced()
    {
        var week = new Scenario();
        await week.FirstRunAsync();
        var key = MobilityKey(Sam);
        week.Calendar.OwnerDeletes(key, Sam);
        await week.RunAsync(TheWeek());
        var createsAfterTheSkip = week.Calendar.CreateCalls;

        // The phone restarts: a new runner on the record as it was stored.
        var restarted = new PlacementRunner(week.Calendar, new MemoryKeyStore { Json = week.Store.Json });
        var afterRestart = await restarted.RunAsync(TheWeek(), CancellationToken.None);
        Assert.Equal(key, Assert.Single(afterRestart.Plan!.Skipped).Key);

        // The planner now wants the same session on a Friday: it is still the session the owner skipped.
        var moved = PlaceRequest(
            Calendars(),
            PlanOf(Strength, Planned(4, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Sam)),
            Cook(Lee, 2, (16, 0), (17, 30), 45));
        var afterReplan = await week.RunAsync(moved);
        Assert.Equal(key, Assert.Single(afterReplan.Plan!.Skipped).Key);
        Assert.Empty(afterReplan.Created);
        Assert.Equal(createsAfterTheSkip, week.Calendar.CreateCalls);
        Assert.Null(week.Calendar.By(key, Sam));
        Assert.True(WrittenKeysCodec.Decode(week.Store.Json).Keys.Contains(key), "The record forgot the skipped session.");

        // A session that was never written is not skipped: only the owner's own deletes are remembered.
        var extra = PlaceRequest(
            Calendars(),
            PlanOf(Strength, Mobility, Planned(4, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Sam)),
            Cook(Lee, 2, (16, 0), (17, 30), 45));
        var withNew = await week.RunAsync(extra);
        var made = Assert.Single(withNew.Created);
        Assert.Equal(MobilityKey(Sam, 1), made.Key);
        Assert.Equal(At(4, 16), made.Start);
        Assert.Equal(key, Assert.Single(withNew.Plan!.Skipped).Key);
    }

    [Fact]
    public async Task ADeleteAndAMoveInOneWeek()
    {
        var week = new Scenario();
        await week.FirstRunAsync();
        week.Calendar.OwnerDeletes(StrengthKey(Sam), Sam);
        week.Calendar.OwnerMoves(HouseholdKey(Lee, "dinner-a"), Lee, At(2, 15, 45), At(2, 16, 30));

        var run = await week.RunAsync(TheWeek());

        Assert.Equal(StrengthKey(Sam), Assert.Single(run.Plan!.Skipped).Key);
        Assert.Equal(At(2, 15, 45), Assert.IsType<EventHeld>(run.Plan.For(HouseholdKey(Lee, "dinner-a"))).Start);
        Assert.Empty(run.Created);
    }

    // ---- no notice, no question ----

    [Fact]
    public async Task NoNotice()
    {
        var week = new Scenario();
        await week.FirstRunAsync();
        week.Calendar.OwnerMoves(StrengthKey(Sam), Sam, At(3, 16, 30), At(3, 17, 10));
        week.Calendar.OwnerDeletes(MobilityKey(Sam), Sam);

        var run = await week.RunAsync(TheWeek());

        // Behaviour: the edits produced a held event and a skipped one, and nothing else at all.
        Assert.Null(run.BlockedBecause);
        Assert.Empty(run.Failed);
        Assert.Empty(run.Created);
        Assert.Empty(run.Plan!.Unplaced);
        Assert.Single(run.Plan.Skipped);
        Assert.Equal(2, run.Plan.Held.Count);

        // Shape: those outcomes have nothing to hold a notice, a question or a reason in.
        Assert.Equal(new[] { "Key", "Person", "Start", "End", "Title" }.Order(), PublicProperties(typeof(EventHeld)).Order());
        Assert.Equal(new[] { "Key", "Person", "Title" }.Order(), PublicProperties(typeof(EventSkipped)).Order());

        // Vocabulary: nothing public in the placement namespace speaks to the owner.
        var talks = new Regex("Notice|Question|Ask|Prompt|Confirm|Alert|Warn|Toast|Dialog|Notify", RegexOptions.CultureInvariant);
        var names = PlacementTypes().SelectMany(Names).Where(name => talks.IsMatch(name)).Distinct().Order().ToList();
        Assert.True(names.Count == 0, "Placement names something that speaks to the owner: " + string.Join(", ", names));
    }

    [Fact]
    public void PlacementNeverReachesTheNoticeBoard()
    {
        var files = ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Domain/Placement/", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count >= 6, $"The guard found only {files.Count} placement files; it is not reading them.");

        var offenders = files
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"Domain\.Notices|NoticeBoard|INoticeBoardAccess"))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "Placement reaches the notice board: " + string.Join(", ", offenders));
    }

    private static IEnumerable<Type> PlacementTypes() =>
        typeof(PlacementKey).Assembly.GetTypes()
            .Where(type => type.Namespace == "Constitution.Domain.Placement" && type.IsPublic)
            .Where(type => type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null);

    private static IEnumerable<string> PublicProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.FlattenHierarchy)
            .Concat(type.BaseType!.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.Name != "EqualityContract")
            .Select(property => property.Name)
            .Distinct();

    private static IEnumerable<string> Names(Type type)
    {
        yield return type.Name;
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return member.Name;
            var parameters = member is MethodBase method ? method.GetParameters() : [];
            foreach (var parameter in parameters)
            {
                yield return parameter.Name ?? string.Empty;
            }
        }

        if (type.IsEnum)
        {
            foreach (var name in Enum.GetNames(type))
            {
                yield return name;
            }
        }
    }
}
