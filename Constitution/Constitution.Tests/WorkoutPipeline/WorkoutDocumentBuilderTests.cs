using Codex.Domain.Music;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Documents;
using Xunit.Sdk;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// A planned session becomes the model the workout document draws. The plan says which exercises, how long and when;
/// the doses, loads, rests and Score settings come from the ladder and from the kind of session, and the builder fills in
/// only that. Every expected figure here is written out.
/// </summary>
public class WorkoutDocumentBuilderTests
{
    private static readonly DateTimeOffset Slot = Est(11, 3, 18);

    private static Session Strength(params Exercise[] exercises) => new(SessionKind.Strength, TimeSpan.FromMinutes(40), false, exercises);

    private static PlannedSession Placed(Session session, Together together = Together.None, params string[] who) =>
        PlannedAt(1, (16, 0), (20, 0), (18, 0), session, together, who.Length == 0 ? [Sam] : who);

    private static WorkoutDocumentModel Build(PlannedSession planned, IReadOnlyList<ExercisePrescription>? doses = null, IReadOnlyList<EncouragementLine?>? voice = null, Soreness? sore = null, IReadOnlyList<ExerciseSubstitution>? swaps = null) =>
        WorkoutDocumentBuilder.Build(planned, Slot, Zone, doses, voice, sore, swaps);

    private static IReadOnlyList<ExercisePrescription> Prescribed(Session session, ProgressionLedger? ledger = null) =>
        ProgressionPlan.Prescribe(ledger ?? ProgressionLedger.Empty, Sam, session);

    [Theory]
    [InlineData(SessionKind.Strength, Together.None, "Strength")]
    [InlineData(SessionKind.OutdoorCardio, Together.None, "Outdoor cardio")]
    [InlineData(SessionKind.Treadmill, Together.None, "Treadmill")]
    [InlineData(SessionKind.Mobility, Together.None, "Mobility")]
    [InlineData(SessionKind.GentleWalk, Together.None, "Easy walk")]
    [InlineData(SessionKind.GentleWalk, Together.FamilyWalk, "Family walk")]
    [InlineData(SessionKind.Strength, Together.AfterBedtime, "Strength together")]
    public void TheTitleNamesTheKindOfSession(SessionKind kind, Together together, string title)
    {
        var session = kind == SessionKind.Strength
            ? Strength(new Exercise("Sit to stand from a chair"))
            : new Session(kind, TimeSpan.FromMinutes(30), false, [new Exercise("Easy walk")]);
        var planned = Placed(session, together, together == Together.None ? [Sam] : [Sam, Lee]);

        var model = Build(planned, kind == SessionKind.Strength ? Prescribed(session) : null);

        Assert.Equal(title, model.Title);
    }

    [Fact]
    public void TheStartIsTheSlotOnTheHouseholdClock()
    {
        var session = Strength(new Exercise("Sit to stand from a chair"));

        // Instant 23:00 UTC on 3 November is 6 pm in the household zone (UTC-5 after the clocks went back).
        var model = WorkoutDocumentBuilder.Build(Placed(session), new DateTimeOffset(2026, 11, 3, 23, 0, 0, TimeSpan.Zero), Zone, Prescribed(session));

        Assert.Equal(new DateTime(2026, 11, 3, 18, 0, 0), model.Start);
    }

    [Fact]
    public void TheTotalIsTheSessionsWholeMinutes()
    {
        var session = Strength(new Exercise("Sit to stand from a chair"));

        Assert.Equal(40, Build(Placed(session), Prescribed(session)).TotalMinutes);
    }

    [Fact]
    public void AStrengthExerciseIsDosedFromTheLadder()
    {
        var session = Strength(new Exercise("Sit to stand from a chair"), new Exercise("Wall angels"));

        var model = Build(Placed(session), Prescribed(session));

        Assert.Equal(["2 × 6", "2 × 6"], model.Exercises.Select(item => item.Dose));
        Assert.All(model.Exercises, item => Assert.Null(item.Load.Pounds));
        Assert.Equal("bodyweight", model.Exercises[0].Load.Description);
    }

    [Fact]
    public void ADumbbellLiftPrintsItsPoundsAndNothingElseDoes()
    {
        var session = Strength(
            new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId),
            new Exercise("Band row", EquipmentCatalogue.BandsId),
            new Exercise("Dead bug on the mat", EquipmentCatalogue.MatId));

        var model = Build(Placed(session), Prescribed(session));

        Assert.Equal("2 × 8", model.Exercises[0].Dose);
        Assert.Equal(10, model.Exercises[0].Load.Pounds);
        Assert.Equal("dumbbell or kettlebell", model.Exercises[0].Load.Description);
        Assert.Null(model.Exercises[1].Load.Pounds);
        Assert.Equal("resistance band", model.Exercises[1].Load.Description);
        Assert.Null(model.Exercises[2].Load.Pounds);
        Assert.Equal("mat", model.Exercises[2].Load.Description);
        Assert.Equal("10 lb dumbbell or kettlebell", WorkoutDocument.LoadText(model.Exercises[0].Load));
    }

    [Fact]
    public void AWeightTheLedgerHasRaisedIsThePoundsOnThePage()
    {
        // Two easy ratings in a row move the lift up one step (10 to 15 lb).
        var exercise = new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId);
        var session = Strength(exercise);
        var pages = new SessionPages(Sam, new DateOnly(2026, 10, 27), SessionKind.Strength, [new ExercisePageRef(exercise, "done", "feel")]);
        var ledger = ProgressionLedger.Empty;
        foreach (var dayOffset in Enumerable.Range(0, 2))
        {
            // Two different days: a day is learned from once, so the same day twice is one easy rating.
            ledger = ledger.Settle(pages with { Day = pages.Day.AddDays(dayOffset) }, [new InkResolution.PageDone("done", "easy")]);
        }

        var model = Build(Placed(session), Prescribed(session, ledger));

        Assert.Equal(15, model.Exercises[0].Load.Pounds);
    }

    [Theory]
    [InlineData(SessionKind.Mobility, 20, 4, new[] { 5, 5, 5, 5 })]
    [InlineData(SessionKind.Mobility, 25, 4, new[] { 6, 6, 6, 7 })]
    [InlineData(SessionKind.OutdoorCardio, 35, 3, new[] { 5, 25, 5 })]
    [InlineData(SessionKind.Treadmill, 30, 3, new[] { 5, 20, 5 })]
    [InlineData(SessionKind.OutdoorCardio, 20, 3, new[] { 5, 10, 5 })]
    [InlineData(SessionKind.GentleWalk, 30, 1, new[] { 30 })]
    public void TimedDosesAddUpToTheSession(SessionKind kind, int minutes, int count, int[] expected)
    {
        var exercises = Enumerable.Range(1, count).Select(number => new Exercise($"Move {number}")).ToList();
        var session = new Session(kind, TimeSpan.FromMinutes(minutes), kind != SessionKind.Mobility, exercises);

        var model = Build(Placed(session));

        Assert.Equal(expected.Select(value => $"{value} min"), model.Exercises.Select(item => item.Dose));
        Assert.Equal(minutes, expected.Sum());
        Assert.All(model.Exercises, item => Assert.Null(item.Load.Pounds));
    }

    [Fact]
    public void ASessionTooShortToCarveItsWarmUpIsSplitEvenly()
    {
        var session = new Session(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(12), true, [new("A"), new("B"), new("C")]);

        var model = Build(Placed(session));

        Assert.Equal(["4 min", "4 min", "4 min"], model.Exercises.Select(item => item.Dose));
    }

    [Fact]
    public void OnlyStrengthHasARestBetweenSets()
    {
        var strength = Strength(new Exercise("Sit to stand from a chair"));
        var mobility = new Session(SessionKind.Mobility, TimeSpan.FromMinutes(20), false, [new Exercise("Cat and cow", EquipmentCatalogue.MatId)]);

        Assert.Equal(60, Build(Placed(strength), Prescribed(strength)).Exercises[0].RestSeconds);
        Assert.Null(Build(Placed(mobility)).Exercises[0].RestSeconds);
    }

    [Fact]
    public void ScoreSettingIsTheBandDefaultByEffort()
    {
        var strength = Strength(new Exercise("Sit to stand from a chair"));
        var walk = new Session(SessionKind.GentleWalk, TimeSpan.FromMinutes(30), false, [new Exercise("Easy walk")]);
        var mobility = new Session(SessionKind.Mobility, TimeSpan.FromMinutes(20), false, [new Exercise("Cat and cow", EquipmentCatalogue.MatId)]);
        var cardio = new Session(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(35), true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]);

        Assert.Equal(TempoBands.Default(TempoBand.Steady), Build(Placed(strength), Prescribed(strength)).Exercises[0].ScoreBpm);
        Assert.Equal(TempoBands.Default(TempoBand.Easy), Build(Placed(walk)).Exercises[0].ScoreBpm);
        Assert.Equal(TempoBands.Default(TempoBand.Easy), Build(Placed(mobility)).Exercises[0].ScoreBpm);
        Assert.Equal(
            [TempoBands.Default(TempoBand.Easy), TempoBands.Default(TempoBand.Steady), TempoBands.Default(TempoBand.Easy)],
            Build(Placed(cardio)).Exercises.Select(item => item.ScoreBpm));
    }

    [Fact]
    public void TheEncouragementGoesToTheExerciseItWasAskedFor()
    {
        var session = Strength(new Exercise("Sit to stand from a chair"), new Exercise("Wall angels"));

        var model = Build(Placed(session), Prescribed(session), voice: [null, new EncouragementLine("15 lb today, up from 10.", true)]);

        Assert.Null(model.Exercises[0].Encouragement);
        Assert.Equal("15 lb today, up from 10.", model.Exercises[1].Encouragement);
    }

    [Fact]
    public void TheVoicedFlagFollowsTheSeatResult()
    {
        var session = Strength(new Exercise("Sit to stand from a chair"), new Exercise("Wall angels"), new Exercise("Hip hinge with hands on hips"));

        var model = Build(
            Placed(session), Prescribed(session),
            voice: [new EncouragementLine("A voiced one.", true), new EncouragementLine("15 lb today, up from 10.", false), null]);

        Assert.True(model.Exercises[0].EncouragementVoiced);
        Assert.False(model.Exercises[1].EncouragementVoiced);
        Assert.Equal("15 lb today, up from 10.", model.Exercises[1].Encouragement);
    }

    [Fact]
    public void ASubstitutionRidesOnTheExerciseThatStoodIn()
    {
        var session = Strength(new Exercise("Seated march in a chair"), new Exercise("Wall angels"));
        var swaps = new[] { new ExerciseSubstitution("Reverse lunges", "Seated march in a chair", "In place of reverse lunges") };

        var model = Build(Placed(session), Prescribed(session), swaps: swaps);

        Assert.Equal("In place of reverse lunges", model.Exercises[0].Substitution);
        Assert.Null(model.Exercises[1].Substitution);
    }

    [Fact]
    public void TheSorenessNoteIsCarriedToTheDocument()
    {
        var session = Strength(new Exercise("Wall angels"));
        var sore = new Soreness("left knee", "Nov 1", 3, false);

        Assert.Same(sore, Build(Placed(session), Prescribed(session), sore: sore).Soreness);
        Assert.Null(Build(Placed(session), Prescribed(session)).Soreness);
    }

    [Fact]
    public void TheEquipmentLineNamesTheKitTheSessionNeeds()
    {
        var kit = Strength(
            new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId),
            new Exercise("Dead bug on the mat", EquipmentCatalogue.MatId));
        var bare = Strength(new Exercise("Sit to stand from a chair"));

        Assert.Equal("Needs dumbbells or a kettlebell, mat", Build(Placed(kit), Prescribed(kit)).EquipmentLine);
        Assert.Equal("No equipment needed", Build(Placed(bare), Prescribed(bare)).EquipmentLine);
    }

    [Fact]
    public void FewerDosesThanExercisesIsRefusedNotGuessed()
    {
        var session = Strength(new Exercise("Sit to stand from a chair"), new Exercise("Wall angels"));

        Assert.Throws<ArgumentException>(() => Build(Placed(session), Prescribed(session).Take(1).ToList()));
        Assert.Throws<ArgumentException>(() => Build(Placed(session)));
    }

    [Fact]
    public void EverySessionThePlannerEverMakesDrawsOnItsPages()
    {
        var kits = new[] { EquipmentSet.Everything(), EquipmentSet.Of([]), EquipmentSet.Of([EquipmentCatalogue.MatId, EquipmentCatalogue.BandsId]) };
        var drawn = 0;
        var kinds = new HashSet<SessionKind>();
        foreach (var kit in kits)
        {
            foreach (var shift in Enumerable.Range(0, 6))
            {
                var calendars = Calendars();
                var plan = Plan(calendars, kit, weekShift: shift);
                foreach (var week in plan.Plans)
                {
                    foreach (var session in week.Sessions)
                    {
                        var doses = session.Session.Kind == SessionKind.Strength
                            ? ProgressionPlan.Prescribe(ProgressionLedger.Empty, week.Person, session.Session)
                            : null;
                        var model = WorkoutDocumentBuilder.Build(session, session.Start, Zone, doses);
                        var pdf = ConstitutionDocumentRenderer.Render(WorkoutDocument.Build(model));
                        Assert.True(pdf.Length > 0, $"{session.Session.Kind} rendered nothing.");
                        kinds.Add(session.Session.Kind);
                        drawn++;
                    }
                }
            }
        }

        Assert.True(drawn >= 30, $"Only {drawn} sessions were drawn; the week should hold more than that.");
        Assert.Contains(SessionKind.Strength, kinds);
        Assert.Contains(SessionKind.Mobility, kinds);
        Assert.Contains(SessionKind.GentleWalk, kinds);
    }
}
