using Codex.Domain.Music;
using System.Text.Json;
using System.Text.Json.Serialization;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>The report the weekly run leaves for the Workouts tab and Today: its record, its codec and its file.</summary>
public sealed class WorkoutWeekReportStoreTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-report-");

    public void Dispose() => folder.Delete(recursive: true);

    private string PathOf() => Path.Combine(folder.FullName, "nested", WorkoutWeekFiles.ReportFile);

    private static StoredWorkoutWeek Week() => new(
        new DateOnly(2026, 11, 1),
        Est(11, 4, 6, 45),
        [new StoredSession(Sam, new DateOnly(2026, 11, 4), Est(11, 4, 16), "Strength", 40, false, [Sam], "None", ["Squat", "Row"], 132)],
        [new StoredReason(Sam, new DateOnly(2026, 11, 4), "Rain is due, so the walk moves indoors.")],
        [new StoredIssue(Sam, 105703, "Strength", 3, 0, 1)],
        [new StoredToday(Sam, new DateOnly(2026, 11, 4), "Strength", new TimeOnly(16, 0), 40, 132), new StoredToday(Lee, new DateOnly(2026, 11, 4), null, null, null, null)]);

    // ---- the codec ----

    [Fact]
    public void ARecordSurvivesTheCodec()
    {
        var back = WorkoutWeekReportCodec.Decode(WorkoutWeekReportCodec.Encode(Week()));

        var held = Assert.IsType<WorkoutWeekRecordRead.Held>(back);
        Assert.Equal(Week().WeekStart, held.Week.WeekStart);
        Assert.Equal(Week().RanAt, held.Week.RanAt);
        Assert.Equal(Week().Sessions[0].Exercises, held.Week.Sessions[0].Exercises);
        Assert.Equal(Week().Sessions[0] with { Exercises = [], Participants = [] }, held.Week.Sessions[0] with { Exercises = [], Participants = [] });
        Assert.Equal(Week().Reasons, held.Week.Reasons);
        Assert.Equal(Week().Assignments, held.Week.Assignments);
        Assert.Equal(Week().Today, held.Week.Today);
    }

    [Fact]
    public void AnUnknownFieldIsTolerated()
    {
        var json = WorkoutWeekReportCodec.Encode(Week()).Replace("\"version\":1", "\"version\":1,\"somethingNew\":{\"a\":[1,2]}", StringComparison.Ordinal);
        Assert.Contains("somethingNew", json, StringComparison.Ordinal);

        Assert.IsType<WorkoutWeekRecordRead.Held>(WorkoutWeekReportCodec.Decode(json));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":99,\"weekStart\":\"2026-11-02\"}")]
    [InlineData("{\"version\":1,\"weekStart\":\"nonsense\",\"ranAt\":\"x\",\"sessions\":[],\"reasons\":[],\"assignments\":[],\"today\":[]}")]
    [InlineData("{\"version\":1,\"weekStart\":\"2026-11-02\",\"ranAt\":\"2026-11-04T06:45:00-05:00\",\"sessions\":null,\"reasons\":[],\"assignments\":[],\"today\":[]}")]
    public void AGarbledRecordIsUnreadableNeverEmpty(string json)
    {
        var read = WorkoutWeekReportCodec.Decode(json);

        Assert.IsType<WorkoutWeekRecordRead.Unreadable>(read);
    }

    [Fact]
    public void ABlankRecordIsNothingYetOnlyWhenThereIsNoText()
    {
        Assert.IsType<WorkoutWeekRecordRead.NothingYet>(WorkoutWeekReportCodec.Decode(null));
        Assert.IsType<WorkoutWeekRecordRead.Unreadable>(WorkoutWeekReportCodec.Decode("   "));
    }

    [Fact]
    public void TheUnreadableReasonQuotesNothingFromTheFile()
    {
        var read = Assert.IsType<WorkoutWeekRecordRead.Unreadable>(WorkoutWeekReportCodec.Decode("{ Sam's secret"));

        Assert.DoesNotContain("secret", read.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePropertyNamesAreFrozen()
    {
        static IEnumerable<string> Names(Type type) =>
            type.GetConstructors().Single().GetParameters()
                .Select(parameter => type.GetProperty(parameter.Name!)!.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false).Cast<JsonPropertyNameAttribute>().Single().Name);

        Assert.Equal(["weekStart", "ranAt", "sessions", "reasons", "assignments", "today"], Names(typeof(StoredWorkoutWeek)));
        Assert.Equal(["person", "day", "start", "kind", "minutes", "sweaty", "participants", "together", "exercises", "scoreBpm"], Names(typeof(StoredSession)));
        Assert.Equal(["person", "day", "line"], Names(typeof(StoredReason)));
        Assert.Equal(["person", "week", "kind", "weekday", "slot", "ordinal"], Names(typeof(StoredIssue)));
        Assert.Equal(["person", "day", "kind", "start", "minutes", "scoreBpm"], Names(typeof(StoredToday)));
        Assert.Equal(1, WorkoutWeekReportCodec.Version);
        using var document = JsonDocument.Parse(WorkoutWeekReportCodec.Encode(Week()));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
    }

    // ---- the file ----

    [Fact]
    public void NoFileIsNothingYet()
    {
        Assert.IsType<WorkoutWeekRecordRead.NothingYet>(new FileWorkoutWeekReports(PathOf()).Load());
    }

    [Fact]
    public void ARecordSurvivesTheFileAndTheFoldersAreMade()
    {
        new FileWorkoutWeekReports(PathOf()).Save(Week());

        var held = Assert.IsType<WorkoutWeekRecordRead.Held>(new FileWorkoutWeekReports(PathOf()).Load());
        Assert.Equal(Week().Reasons, held.Week.Reasons);
        Assert.Equal([PathOf()], Directory.GetFiles(Path.GetDirectoryName(PathOf())!));
    }

    [Fact]
    public void AnUnreadableFileIsReportedAndLeftWhereItIsOnLoad()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ torn");

        Assert.IsType<WorkoutWeekRecordRead.Unreadable>(new FileWorkoutWeekReports(PathOf()).Load());
        Assert.Equal("{ torn", File.ReadAllText(PathOf()));
    }

    [Fact]
    public void SavingOverAnUnreadableFileSetsItAsideFirst()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ torn");
        var store = new FileWorkoutWeekReports(PathOf());

        store.Save(Week());

        Assert.Equal("{ torn", File.ReadAllText(PathOf() + ".unreadable"));
        Assert.IsType<WorkoutWeekRecordRead.Held>(store.Load());
    }

    [Fact]
    public void ASecondUnreadableFileNeverReplacesTheFirst()
    {
        var store = new FileWorkoutWeekReports(PathOf());
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "first damage");
        store.Save(Week());
        File.WriteAllText(PathOf(), "second damage");

        store.Save(Week());

        Assert.Equal("first damage", File.ReadAllText(PathOf() + ".unreadable"));
        Assert.Equal("second damage", File.ReadAllText(PathOf() + ".unreadable.2"));
    }

    // ---- the record from a run ----

    private static readonly Session Strong = new(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [new("Squat"), new("Row")]);
    private static readonly Session Walk = new(SessionKind.GentleWalk, TimeSpan.FromMinutes(30), false, [new("Easy walk")]);

    [Fact]
    public async Task TheRecordCarriesThePlacedPlanAndTodaysClipping()
    {
        using var rig = new WeekRig();
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Strong), rig.Session(Lee, 2, 7, Walk)));
        var report = await rig.PlannedAsync();

        var record = WorkoutWeekRecordBuilder.From(report, KeyAssignments.Empty, Est(11, 2, 5, 30), new DateOnly(2026, 11, 2));

        Assert.Equal(WeekStart, record.WeekStart);
        Assert.Equal(Est(11, 2, 5, 30), record.RanAt);
        Assert.Equal(2, record.Sessions.Count);
        var sam = Assert.Single(record.Sessions, item => item.Person == Sam);
        Assert.Equal("Strength", sam.Kind);
        Assert.Equal(40, sam.Minutes);
        Assert.Equal(["Squat", "Row"], sam.Exercises);
        Assert.Equal(TempoBands.Default(TempoBand.Steady), sam.ScoreBpm);
        var today = Assert.Single(record.Today, item => item.Person == Sam);
        Assert.Equal("Strength", today.Kind);
        Assert.Equal(new TimeOnly(16, 0), today.Start);
        Assert.Equal(40, today.Minutes);
        Assert.Equal(TempoBands.Default(TempoBand.Steady), today.ScoreBpm);
        var rest = Assert.Single(record.Today, item => item.Person == Lee);
        Assert.Null(rest.Kind);
        Assert.Null(rest.Start);
        Assert.Null(rest.Minutes);
        Assert.Null(rest.ScoreBpm);
    }

    [Fact]
    public async Task AGentleWalkHeadlinesAtTheEasyTempo()
    {
        using var rig = new WeekRig();
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Lee, 0, 7, Walk)));
        var report = await rig.PlannedAsync();

        var record = WorkoutWeekRecordBuilder.From(report, KeyAssignments.Empty, Est(11, 2, 5, 30), new DateOnly(2026, 11, 2));

        Assert.Equal(TempoBands.Default(TempoBand.Easy), Assert.Single(record.Sessions).ScoreBpm);
    }

    [Fact]
    public async Task TheAssignmentsAreTheRecordsOwn()
    {
        using var rig = new WeekRig();
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Strong)));
        var report = await rig.PlannedAsync();
        var issued = KeyAssignments.Empty.With([new KeyIssue(Sam, 105703, SessionKind.Strength, DayOfWeek.Monday, 0, 0)]);

        var record = WorkoutWeekRecordBuilder.From(report, issued, Est(11, 2, 5, 30), new DateOnly(2026, 11, 2));

        var kept = Assert.Single(record.Assignments);
        Assert.Equal(new StoredIssue("sam", 105703, "Strength", (int)DayOfWeek.Monday, 0, 0), kept);
    }

    [Fact]
    public async Task EveryReasonForAChangeIsKeptWithItsDay()
    {
        using var rig = new WeekRig();
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Strong)));
        var report = await rig.PlannedAsync();
        report = report with { DayNotes = [new DayNote(Sam, new DateOnly(2026, 11, 3), "A trip day, so no session.")] };

        var record = WorkoutWeekRecordBuilder.From(report, KeyAssignments.Empty, Est(11, 2, 5, 30), new DateOnly(2026, 11, 2));

        Assert.Contains(new StoredReason(Sam, new DateOnly(2026, 11, 3), "A trip day, so no session."), record.Reasons);
    }
}
