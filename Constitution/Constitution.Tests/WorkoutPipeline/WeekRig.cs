using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Ink;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The weekly run with every seam faked, on the invented household: Sam and Lee, the household week of Sunday 1 November 2026, run on its Monday morning, on
/// standard time. The calendar the run reads never holds the events it wrote (that is the seam's rule), so what the run
/// wrote lives on <see cref="Writer"/> only.
/// </summary>
internal sealed class WeekRig : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-week-");
    private IPlanAuthor author = new NoPlanAuthor();

    public WeekRig()
    {
        Expected = new FileExpectedDocuments(Path.Combine(folder.FullName, "expected.json"));
        Delivered = new FileDeliveredWorkouts(Path.Combine(folder.FullName, "delivered.json"));
        Delivery = new DeliveryService(Tablet, Access, () => Clock);
        Seams = Build();
        Run = new WorkoutWeekRun(Seams);
    }

    public DirectoryInfo Folder => folder;

    public DateTimeOffset Clock { get; set; } = Est(11, 2, 5, 30);

    public FakeCalendarsSource Calendars { get; } = new();

    public FakeLearning Learning { get; } = new();

    public FakeConditions Conditions { get; } = new();

    public FakeHotels Hotels { get; } = new();

    public FakeRecovery Recovery { get; } = new();

    public FakeEncouragement Encouragement { get; } = new();

    public FakeCalendar Writer { get; } = new();

    public MemoryKeyStore Keys { get; } = new();

    public MemoryKeyAssignments KeyAssignments { get; } = new();

    public FakeTablet Tablet { get; } = new();

    public FakeInkProbe InkProbe { get; } = new();

    public Access Access { get; } = new();

    public RecordingWeekLog Log { get; } = new();

    public DeliveryService Delivery { get; }

    public FileExpectedDocuments Expected { get; }

    public FileDeliveredWorkouts Delivered { get; }

    public WorkoutWeekSeams Seams { get; private set; }

    public WorkoutWeekRun Run { get; private set; }

    /// <summary>Plans through this author (held to the planner's rules), falling back to the rules when it breaks them.</summary>
    public void AuthorWith(Func<WorkoutPlanRequest, HouseholdWorkoutPlan> plan)
    {
        author = new FixedAuthor(plan);
        Seams = Build();
        Run = new WorkoutWeekRun(Seams);
    }

    public WorkoutWeekRequest Request(DateTimeOffset? now = null, params string[] deliverFor) =>
        new(WeekStart, now ?? Clock, Rules(), EquipmentSet.Everything(), deliverFor.Length == 0 ? [Sam, Lee] : deliverFor);

    public async Task<WorkoutWeekReport> PlannedAsync(WorkoutWeekRequest? request = null)
    {
        var outcome = await Run.RunAsync(request ?? Request(), CancellationToken.None);
        return Assert.IsType<WorkoutWeekOutcome.Planned>(outcome).Report;
    }

    /// <summary>Both adults have trained lately, so the ordinary plan applies and not the assessment.</summary>
    public void BothAreEstablished()
    {
        var skips = SkipLedger.Empty;
        foreach (var person in new[] { Sam, Lee })
        {
            skips = skips.Settle(new SessionMarks(person, new DateOnly(2026, 10, 28), SessionKind.Strength, ["done"]), [new InkResolution.Ticked("done")]);
        }

        Learning.Held = Learning.Held with
        {
            Skips = skips,
            AssessmentWeeks = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase)
            {
                [Sam] = new DateOnly(2026, 10, 26),
                [Lee] = new DateOnly(2026, 10, 26),
            },
        };
    }

    /// <summary>The person's busy time as the run sees it: on the trip-aware calendars.</summary>
    public BusyTime BusyOf(string person) =>
        BusyTime.For(person, WeekStart, WeekStart.AddDays(6), Rules(), new TripAwareCalendars(Calendars.Snapshot, Rules()));

    /// <summary>The free stretch of the person's day that holds this hour.</summary>
    public FreeGap GapAt(string person, int dayOffset, int hour) =>
        BusyOf(person).FreeGaps(MondayAt(dayOffset, 6, 30), MondayAt(dayOffset, 21, 30), TimeSpan.FromMinutes(30))
            .First(gap => gap.Start <= MondayAt(dayOffset, hour) && MondayAt(dayOffset, hour) < gap.End);

    public PlannedSession Session(string person, int dayOffset, int hour, Session session) =>
        new(MondayDay(dayOffset), MondayAt(dayOffset, hour), GapAt(person, dayOffset, hour), session, [person], Together.None);

    /// <summary>A week for both adults from the sessions given; whoever has none gets an empty week with its reason.</summary>
    public static HouseholdWorkoutPlan PlanOfSessions(params PlannedSession[] sessions)
    {
        var plans = new[] { Sam, Lee }.Select(person =>
        {
            var mine = sessions.Where(session => session.Participants.Contains(person)).OrderBy(session => session.Start).ToList();
            return new WeeklyPlan(person, WeekStart, mine, mine.Count == 0 ? "Nothing is planned." : null);
        }).ToList();
        return new HouseholdWorkoutPlan(WeekStart, plans, []);
    }

    public IEnumerable<string> UploadedTitles => Tablet.Calls.Where(call => call.StartsWith("upload:", StringComparison.Ordinal)).Select(call => call["upload:".Length..]);

    public void Dispose() => folder.Delete(recursive: true);

    private WorkoutWeekSeams Build() => new(
        Calendars,
        Learning,
        new PlanWithFallback(author, new TrailPlanLog(Log)),
        Conditions,
        Hotels,
        Recovery,
        Encouragement,
        Writer,
        Keys,
        KeyAssignments,
        Delivery,
        Tablet,
        InkProbe,
        Expected,
        Delivered,
        Log);
}
