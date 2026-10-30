using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.Placement;
using Constitution.Tests.WorkoutPipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;
using Run = Constitution.Infrastructure.Meals.MealWeekRun;

namespace Constitution.Tests.MealWeekRun;

/// <summary>Sam, Lee, Robin and Juniper's weekly meal run with every seam faked. Every call is written to <see cref="Calls"/> in the order it happened.</summary>
internal sealed class MealWeekRig
{
    public MealWeekRig()
    {
        Box = new FakeBox(this);
        History = new MemoryHistory(this);
        Taste = new FakeTaste(this);
        Calendars = new RecordingCalendars(this);
        Records = new MemoryRecords(this);
        Seams = Build();
    }

    /// <summary>The Wednesday before the household's week of Sunday 25 October 2026, at noon: nothing of the week has passed.</summary>
    public DateTimeOffset Now { get; set; } = Edt(10, 21, 12);

    public List<string> Calls { get; } = [];

    public bool MayPlan { get; set; } = true;

    public string LeaseSentence { get; set; } = "This phone plans this week.";

    public FakeBox Box { get; }

    public MemoryHistory History { get; }

    public FakeTaste Taste { get; }

    public RecordingCalendars Calendars { get; }

    public FakeCalendar Writer { get; } = new();

    public MemoryKeyStore Keys { get; } = new();

    public MemoryKeyAssignments Issued { get; } = new();

    public MemoryRecords Records { get; }

    public RecordingWeekLog Log { get; } = new();

    public RecordingMealLog Origin { get; } = new();

    public MealWeekSeams Seams { get; private set; }

    public MealWeekRequestInputs Inputs(DateOnly? week = null) => new(week ?? Sunday, Now, Rules());

    public async Task<MealWeekOutcome> RunAsync(MealWeekRequestInputs? inputs = null) =>
        await Run.RunAsync(inputs ?? Inputs(), Seams, CancellationToken.None);

    public async Task<MealWeekReport> PlannedAsync(MealWeekRequestInputs? inputs = null)
    {
        var outcome = await RunAsync(inputs);
        return Assert.IsType<MealWeekOutcome.Planned>(outcome).Report;
    }

    public MealWeekOutcome.NotPlanned StoppedWith(MealNotPlannedBecause because, MealWeekOutcome outcome)
    {
        var stopped = Assert.IsType<MealWeekOutcome.NotPlanned>(outcome);
        Assert.Equal(because, stopped.Because);
        return stopped;
    }

    /// <summary>Nothing was asked of the calendar and nothing was kept.</summary>
    public void AssertNothingWritten()
    {
        Assert.Equal(0, Writer.CreateCalls);
        Assert.Equal(0, Writer.FindCalls);
        Assert.Equal(0, Keys.Saves);
        Assert.Equal(0, Issued.Saves);
        Assert.Equal(0, Records.Saves);
        Assert.Equal(0, History.Appends);
    }

    private MealWeekSeams Build() => new(
        cancellation =>
        {
            Calls.Add("lease");
            return Task.FromResult(new PlanningStanding(MayPlan, null, LeaseSentence));
        },
        Box,
        History,
        Taste,
        Calendars,
        new MealWithFallback(new NoMealAuthor(), Origin),
        Writer,
        Keys,
        Issued,
        Records,
        Log);
}

internal sealed class FakeBox : IRecipeBoxSource
{
    private readonly MealWeekRig rig;

    public FakeBox(MealWeekRig rig) => this.rig = rig;

    public RecipeBoxRead Answer { get; set; } = new RecipeBoxRead.Ready(new RecipeBoxSnapshot(Box(), new Dictionary<RecipeId, CardHistory>(), new HashSet<RecipeId>()));

    public RecipeBoxRead Read()
    {
        rig.Calls.Add("recipes");
        return Answer;
    }

    public void Of(IReadOnlyList<Recipe> cards) =>
        Answer = new RecipeBoxRead.Ready(new RecipeBoxSnapshot(cards, new Dictionary<RecipeId, CardHistory>(), new HashSet<RecipeId>()));

    public void Move(CuisineMove move) => throw new InvalidOperationException("A run never moves a card.");

    public CookSoonResult PlaceNextWeek(RecipeId card) => throw new InvalidOperationException("A run never places a card.");
}

/// <summary>The history held as the text the phone would store, so every read goes through the codec.</summary>
internal sealed class MemoryHistory : IMealHistoryStore
{
    private readonly MealWeekRig rig;

    public MemoryHistory(MealWeekRig rig) => this.rig = rig;

    public string? Json { get; set; }

    public int Appends { get; private set; }

    public MealHistoryRead Load()
    {
        rig.Calls.Add("history");
        return Json is null ? new MealHistoryRead([], null) : MealHistoryCodec.Read(Json);
    }

    public void Append(IEnumerable<CookedMeal> meals, DateOnly today) => Appends++;
}

internal sealed class FakeTaste : IMealTasteSource
{
    private readonly MealWeekRig rig;

    public FakeTaste(MealWeekRig rig) => this.rig = rig;

    public MealTasteRead Answer { get; set; } = new MealTasteRead.None();

    public MealTasteRead Read()
    {
        rig.Calls.Add("taste");
        return Answer;
    }
}

/// <summary>Hands back the snapshot it is given and remembers the window it was asked for.</summary>
internal sealed class RecordingCalendars : Constitution.Domain.Workouts.Pipeline.IHouseholdCalendarsSource
{
    private readonly MealWeekRig rig;

    public RecordingCalendars(MealWeekRig rig) => this.rig = rig;

    public HouseholdCalendarSnapshot Snapshot { get; set; } = Constitution.Tests.Calendar.InventedFamily.Calendars();

    public int Reads { get; private set; }

    public DateTimeOffset? From { get; private set; }

    public DateTimeOffset? To { get; private set; }

    public Task<HouseholdCalendarSnapshot> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        rig.Calls.Add("calendars");
        Reads++;
        From = from;
        To = to;
        return Task.FromResult(Snapshot);
    }
}

/// <summary>The saved weeks held as the text the phone would store.</summary>
internal sealed class MemoryRecords : IMealWeekRecords
{
    private readonly MealWeekRig rig;

    public MemoryRecords(MealWeekRig rig) => this.rig = rig;

    public string? Json { get; set; }

    public int Saves { get; private set; }

    public MealWeekRecordRead Load()
    {
        rig.Calls.Add("records");
        return MealWeekRecordCodec.Decode(Json);
    }

    public void Save(MealWeekRecord record)
    {
        Saves++;
        Json = MealWeekRecordCodec.Encode(record);
    }

    public MealWeekRecord Held() => Assert.IsType<MealWeekRecordRead.Held>(MealWeekRecordCodec.Decode(Json)).Record;
}

internal sealed class RecordingMealLog : IMealLog
{
    public List<(Constitution.Domain.Workouts.PlanOrigin Origin, AuthorUnavailable? Reason)> Lines { get; } = [];

    public void Record(Constitution.Domain.Workouts.PlanOrigin origin, AuthorUnavailable? reason) => Lines.Add((origin, reason));
}
