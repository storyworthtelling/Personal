using System.Text.Json;
using Constitution.Domain.Equipment;
using Constitution.Domain.Ink;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// C-5.2.3 · Soreness. A margin note read as soreness changes the plan around the area; every page asks "Still sore?"
/// until a blank (D28); a lasting soreness, or alarming signals, earn the doctor suggestion once (D29). Only injury
/// changes the plan's shape.
///
/// <para>The week is written by hand (<see cref="HandmadePlans"/>), so every expected exercise is a name written here.
/// The place words are invented.</para>
/// </summary>
public class SorenessTests
{
    private static readonly string[] Boxes = ["sore-1", "sore-2", "sore-3"];

    private static SorenessLedger Sore(string place, int noted = 1, string person = Sam) =>
        SorenessLedger.Empty.Note(person, place, Day(noted));

    private static InkResolution[] Blank() => Boxes.Select(id => (InkResolution)new InkResolution.Better(id)).ToArray();

    private static InkResolution[] Marked(string id) =>
        Boxes.Select(box => box == id ? new InkResolution.StillSore(box) : (InkResolution)new InkResolution.Better(box)).ToArray();

    private static SorenessLedger Settled(SorenessLedger ledger, int day, SessionOutcome outcome, params InkResolution[] resolutions) =>
        ledger.Settle(Sam, Day(day), outcome, Boxes, resolutions);

    private static string[] Names(PlannedSession session) => session.Session.Exercises.Select(item => item.Name).ToArray();

    private static SorenessAdjusted Around(SorenessLedger ledger, HouseholdWorkoutPlan plan, EquipmentSet? equipment = null) =>
        SorenessPlan.Apply(plan, ledger, equipment ?? EquipmentSet.Everything());

    private static HouseholdWorkoutPlan StrengthWeek() =>
        HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])], [HandmadePlans.Placed(2, SessionKind.Strength, [Lee])]);

    // ---- WorksAroundTheArea ----

    [Fact]
    public void WorksAroundTheArea()
    {
        var result = Around(Sore("left knee"), StrengthWeek());

        var session = HandmadePlans.OnDay(result.Plan, Sam, 2);
        Assert.Equal(
            ["Glute bridge on the mat", "Push-ups against a wall", "Wall angels", "Hip hinge with hands on hips", "Seated march in a chair"],
            Names(session));
        Assert.All(session.Session.Exercises, exercise => Assert.DoesNotContain(SoreArea.Knee, ExerciseLoads.Of(exercise)));

        var adjustment = Assert.Single(result.Adjustments);
        Assert.Equal(Sam, adjustment.Person);
        Assert.Equal(Day(2), adjustment.Day);
        Assert.True(adjustment.EasyTempo);
        Assert.False(adjustment.Removed);
        Assert.Contains("left knee", adjustment.Reason);
        Assert.Equal(
            [
                new ExerciseSubstitution("Sit to stand from a chair", "Glute bridge on the mat", "In place of sit to stand from a chair"),
                new ExerciseSubstitution("Standing knee lift", "Seated march in a chair", "In place of standing knee lift"),
            ],
            adjustment.Substitutions.ToArray());
    }

    [Fact]
    public void ASessionKeepsItsKindItsSlotAndItsLength()
    {
        var plan = StrengthWeek();

        var after = HandmadePlans.OnDay(Around(Sore("left knee"), plan).Plan, Sam, 2);
        var before = HandmadePlans.OnDay(plan, Sam, 2);

        Assert.NotEqual(Names(before), Names(after));
        Assert.Equal(before.Session.Kind, after.Session.Kind);
        Assert.Equal(before.Session.Duration, after.Session.Duration);
        Assert.Equal(before.Start, after.Start);
        Assert.Equal(before.Gap, after.Gap);
        Assert.Equal(before.Participants, after.Participants);
    }

    [Fact]
    public void NeverPrescribesUntickedEquipmentWhileWorkingAround()
    {
        var none = Around(Sore("left knee"), StrengthWeek(), EquipmentSet.Of([]));

        var session = HandmadePlans.OnDay(none.Plan, Sam, 2);

        Assert.DoesNotContain("Glute bridge on the mat", Names(session));
        Assert.Equal(
            ["Seated march in a chair", "Push-ups against a wall", "Wall angels", "Hip hinge with hands on hips", "Standing calf raise holding a counter"],
            Names(session));
        Assert.Empty(session.Session.EquipmentNeeded);
    }

    [Fact]
    public void ASoreShoulderMovesThePushesAndPulls()
    {
        var result = Around(Sore("right shoulder"), StrengthWeek());

        var session = HandmadePlans.OnDay(result.Plan, Sam, 2);

        Assert.Equal(
            ["Sit to stand from a chair", "Wrist circles and fist squeezes", "Seated chin tuck", "Hip hinge with hands on hips", "Standing knee lift"],
            Names(session));
        Assert.All(session.Session.Exercises, exercise => Assert.DoesNotContain(SoreArea.Shoulder, ExerciseLoads.Of(exercise)));
    }

    [Fact]
    public void ASubstituteNeverRepeatsAnExerciseInTheSession()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam], exercises:
        [
            new("Glute bridge on the mat", EquipmentCatalogue.MatId),
            new("Sit to stand from a chair"),
            new("Standing knee lift"),
        ])]);

        var session = HandmadePlans.OnDay(Around(Sore("left knee"), plan).Plan, Sam, 2);

        Assert.DoesNotContain("Sit to stand from a chair", Names(session));
        Assert.DoesNotContain("Standing knee lift", Names(session));
        Assert.Equal(3, Names(session).Length);
        Assert.Equal(session.Session.Exercises.Count, Names(session).Distinct().Count());
    }

    [Fact]
    public void AnExerciseNobodyHasHeardOfIsTreatedAsLoadingTheArea()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam], exercises: [new("Archer squat with a twist"), new("Push-ups against a wall")])]);

        var result = Around(Sore("left knee"), plan);

        Assert.DoesNotContain("Archer squat with a twist", Names(HandmadePlans.OnDay(result.Plan, Sam, 2)));
        Assert.Equal("Archer squat with a twist", Assert.Single(Assert.Single(result.Adjustments).Substitutions).Original);
    }

    [Fact]
    public void EveryExerciseThePlannerHasEverPlannedIsKnown()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var equipment in new[] { EquipmentSet.Everything(), EquipmentSet.Of([]), EquipmentSet.Of([EquipmentCatalogue.MatId, EquipmentCatalogue.BandsId]), EquipmentSet.Of([EquipmentCatalogue.BenchId, EquipmentCatalogue.PullUpBarId, EquipmentCatalogue.TreadmillId]) })
        {
            foreach (var shift in Enumerable.Range(0, 6).Select(week => week * 7))
            {
                var plan = Plan(Calendars(), equipment, weekShift: shift);
                foreach (var exercise in plan.Plans.SelectMany(week => week.Sessions).SelectMany(session => session.Session.Exercises))
                {
                    names.Add(exercise.Name);
                }
            }
        }

        Assert.True(names.Count >= 20, "Too few exercises were planned for this to prove the table covers the library.");
        foreach (var name in names)
        {
            Assert.True(ExerciseLoads.IsKnown(name), $"'{name}' has no row in the loads table, so soreness would treat it as loading everything.");
        }
    }

    [Fact]
    public void TheAssessmentExercisesAreKnownToo()
    {
        var request = Request(Calendars(), bothAdults: false);
        var week = AssessmentPlanner.Plan(request);

        var names = week.Plan.Plans.SelectMany(plan => plan.Sessions).SelectMany(session => session.Session.Exercises).Select(item => item.Name).Distinct().ToList();

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.True(ExerciseLoads.IsKnown(name), $"'{name}' is not in the loads table."));
    }

    // ---- sessions that are not exercise lists ----

    [Fact]
    public void ASoreKneeRestsTheWalks()
    {
        var plan = HandmadePlans.Week(
        [
            HandmadePlans.Placed(2, SessionKind.OutdoorCardio, [Sam]),
            HandmadePlans.Placed(3, SessionKind.Treadmill, [Sam]),
            HandmadePlans.Placed(6, SessionKind.GentleWalk, [Sam]),
        ]);

        var result = Around(Sore("left knee"), plan);

        Assert.Empty(result.Plan.PlanFor(Sam)!.Sessions);
        Assert.Equal(3, result.Adjustments.Count);
        Assert.All(result.Adjustments, item => Assert.True(item.Removed));
    }

    [Fact]
    public void ASoreShoulderKeepsTheWalksAndEasesThem()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.OutdoorCardio, [Sam])]);

        var result = Around(Sore("right shoulder"), plan);

        Assert.Equal(HandmadePlans.OnDay(plan, Sam, 2), HandmadePlans.OnDay(result.Plan, Sam, 2));
        var adjustment = Assert.Single(result.Adjustments);
        Assert.True(adjustment.EasyTempo);
        Assert.False(adjustment.Removed);
        Assert.Empty(adjustment.Substitutions);
    }

    [Fact]
    public void APlaceNobodyCanPlaceRestsTheHardDays()
    {
        var plan = HandmadePlans.Week(
        [
            HandmadePlans.Placed(1, SessionKind.Strength, [Sam]),
            HandmadePlans.Placed(2, SessionKind.OutdoorCardio, [Sam]),
            HandmadePlans.Placed(3, SessionKind.Mobility, [Sam]),
            HandmadePlans.Placed(6, SessionKind.GentleWalk, [Sam]),
        ]);

        var result = Around(Sore("somewhere in my side"), plan);

        Assert.Equal([SessionKind.Mobility, SessionKind.GentleWalk], result.Plan.PlanFor(Sam)!.Sessions.Select(item => item.Session.Kind).ToArray());
        Assert.Equal(2, result.Adjustments.Count(item => item.Removed));
        Assert.Equal(2, result.Adjustments.Count(item => item is { Removed: false, EasyTempo: true }));
    }

    [Fact]
    public void WhenNothingCanBeDoneAroundItTheDayIsRest()
    {
        var everywhere = new[] { "neck", "shoulder", "elbow", "upper back", "lower back", "hip", "thigh", "knee", "ankle" }
            .Aggregate(SorenessLedger.Empty, (ledger, place) => ledger.Note(Sam, place, Day(1)));
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])]);

        var result = Around(everywhere, plan);

        Assert.Empty(result.Plan.PlanFor(Sam)!.Sessions);
        Assert.True(Assert.Single(result.Adjustments).Removed);
    }

    // ---- who and when ----

    [Fact]
    public void OnlyInjuryChangesThePlansShape()
    {
        var plan = Plan(Calendars());

        var result = Around(SorenessLedger.Empty, plan);

        Assert.Same(plan, result.Plan);
        Assert.Empty(result.Adjustments);
    }

    [Fact]
    public void SomeoneElsesSorenessChangesNothingForYou()
    {
        var result = Around(Sore("left knee", person: Lee), StrengthWeek());

        Assert.Equal(HandmadePlans.OnDay(StrengthWeek(), Sam, 2).Session.Exercises.Select(item => item.Name), Names(HandmadePlans.OnDay(result.Plan, Sam, 2)));
        Assert.Equal(Lee, Assert.Single(result.Adjustments).Person);
    }

    [Fact]
    public void SessionsBeforeTheNoteAreLeftAlone()
    {
        var plan = HandmadePlans.Week(
        [
            HandmadePlans.Placed(1, SessionKind.Strength, [Sam]),
            HandmadePlans.Placed(3, SessionKind.Strength, [Sam]),
            HandmadePlans.Placed(5, SessionKind.Strength, [Sam]),
        ]);

        var result = Around(Sore("left knee", noted: 3), plan);

        Assert.Equal(HandmadePlans.OnDay(plan, Sam, 1), HandmadePlans.OnDay(result.Plan, Sam, 1));
        Assert.NotEqual(Names(HandmadePlans.OnDay(plan, Sam, 3)), Names(HandmadePlans.OnDay(result.Plan, Sam, 3)));
        Assert.NotEqual(Names(HandmadePlans.OnDay(plan, Sam, 5)), Names(HandmadePlans.OnDay(result.Plan, Sam, 5)));
        Assert.Equal([Day(3), Day(5)], result.Adjustments.Select(item => item.Day).ToArray());
    }

    [Fact]
    public void ASharedSessionWorksAroundEveryoneInIt()
    {
        var joint = HandmadePlans.Placed(2, SessionKind.Strength, [Sam, Lee], Together.AfterBedtime, hour: 20);
        var plan = HandmadePlans.Week([], [], [joint]);
        var ledger = Sore("left knee", person: Sam).Note(Lee, "right shoulder", Day(1));

        var result = Around(ledger, plan);

        var shared = Assert.Single(result.Plan.JointSessions);
        Assert.Same(shared, HandmadePlans.OnDay(result.Plan, Sam, 2));
        Assert.Same(shared, HandmadePlans.OnDay(result.Plan, Lee, 2));
        Assert.All(shared.Session.Exercises, exercise =>
        {
            Assert.DoesNotContain(SoreArea.Knee, ExerciseLoads.Of(exercise));
            Assert.DoesNotContain(SoreArea.Shoulder, ExerciseLoads.Of(exercise));
        });
        Assert.Equal([Lee, Sam], result.Adjustments.Select(item => item.Person).Order().ToArray());
    }

    [Fact]
    public void ASharedSessionIsAdjustedEvenWhenOnlyOneIsSore()
    {
        var joint = HandmadePlans.Placed(2, SessionKind.Strength, [Sam, Lee], Together.AfterBedtime, hour: 20);

        var result = Around(Sore("left knee", person: Lee), HandmadePlans.Week([], [], [joint]));

        Assert.DoesNotContain("Sit to stand from a chair", Names(Assert.Single(result.Plan.JointSessions)));
        Assert.Equal(Lee, Assert.Single(result.Adjustments).Person);
    }

    [Fact]
    public void ClearedSorenessGivesTheOriginalPlanBack()
    {
        var plan = StrengthWeek();
        var sore = Sore("left knee");
        Assert.NotSame(plan, Around(sore, plan).Plan);

        var cleared = Settled(sore, 2, SessionOutcome.Done, Blank());

        Assert.Same(plan, Around(cleared, plan).Plan);
    }

    // ---- BlankClearsIt ----

    [Fact]
    public void BlankClearsIt()
    {
        var sore = Sore("left knee");
        Assert.Single(sore.OpenFor(Sam));

        var cleared = Settled(sore, 2, SessionOutcome.Done, Blank());

        Assert.Empty(cleared.OpenFor(Sam));
        Assert.Null(cleared.PageModel(Sam, Day(3)));
    }

    [Fact]
    public void AMarkOnAnyPageKeepsItOpen()
    {
        var sore = Sore("left knee");

        foreach (var id in Boxes)
        {
            var kept = Settled(sore, 2, SessionOutcome.Done, Marked(id));
            Assert.Single(kept.OpenFor(Sam));
        }
    }

    [Fact]
    public void ABlankOnTheDayItWasNotedIsNotAnAnswer()
    {
        // The page that day could not have carried the question: it was printed before the note was made.
        var kept = Settled(Sore("left knee", noted: 2), 2, SessionOutcome.Done, Blank());

        Assert.Single(kept.OpenFor(Sam));
    }

    [Fact]
    public void ABlankBeforeTheNoteIsNotAnAnswerEither()
    {
        var kept = Settled(Sore("left knee", noted: 3), 1, SessionOutcome.Done, Blank());

        Assert.Single(kept.OpenFor(Sam));
    }

    [Fact]
    public void ABlankClearsEvenOnASkippedDay()
    {
        // D28 literally: a blank always means better, whether or not the workout was done.
        var sore = Sore("left knee");

        Assert.Empty(Settled(sore, 2, SessionOutcome.Skipped, Blank()).OpenFor(Sam));
        Assert.Empty(Settled(sore, 2, SessionOutcome.Unsettled, Blank()).OpenFor(Sam));
    }

    [Fact]
    public void AMarkStillKeepsItOnASkippedDay()
    {
        var sore = Sore("left knee");

        Assert.Single(Settled(sore, 2, SessionOutcome.Skipped, Marked("sore-2")).OpenFor(Sam));
        Assert.Single(Settled(sore, 2, SessionOutcome.Unsettled, Marked("sore-2")).OpenFor(Sam));
    }

    [Fact]
    public void ASkippedBlankBeforeTheNoteStillDoesNotClear()
    {
        Assert.Single(Settled(Sore("left knee", noted: 3), 1, SessionOutcome.Skipped, Blank()).OpenFor(Sam));
        Assert.Single(Settled(Sore("left knee", noted: 2), 2, SessionOutcome.Skipped, Blank()).OpenFor(Sam));
    }

    [Fact]
    public void AnUnreadPageOrAnOpenQuestionKeepsItOpen()
    {
        var sore = Sore("left knee");
        InkResolution[] unread = [new InkResolution.Better("sore-1"), new InkResolution.NotRead("sore-2", "page not read"), new InkResolution.Better("sore-3")];
        InkResolution[] asking = [new InkResolution.Better("sore-1"), new InkResolution.NeedsAsking("sore-2", "Still sore?", "Still sore", "Better", "cause"), new InkResolution.Better("sore-3")];
        InkResolution[] waiting = [new InkResolution.Better("sore-1"), new InkResolution.Undecided("sore-2")];

        Assert.Single(Settled(sore, 2, SessionOutcome.Done, unread).OpenFor(Sam));
        Assert.Single(Settled(sore, 2, SessionOutcome.Done, asking).OpenFor(Sam));
        Assert.Single(Settled(sore, 2, SessionOutcome.Done, waiting).OpenFor(Sam));
        Assert.Single(Settled(sore, 2, SessionOutcome.Skipped, unread).OpenFor(Sam));
        Assert.Single(Settled(sore, 2, SessionOutcome.Unsettled, asking).OpenFor(Sam));
        Assert.Single(Settled(sore, 2, SessionOutcome.Skipped, waiting).OpenFor(Sam));
    }

    [Fact]
    public void NoResolutionsForTheBoxesIsNoAnswer()
    {
        var sore = Sore("left knee");

        Assert.Single(Settled(sore, 2, SessionOutcome.Done).OpenFor(Sam));
        Assert.Single(Settled(sore, 2, SessionOutcome.Done, new InkResolution.Better("some-other-box")).OpenFor(Sam));
    }

    [Fact]
    public void ABlankOnlyClearsThatPersonsNote()
    {
        var sore = Sore("left knee").Note(Lee, "right shoulder", Day(1));

        var cleared = Settled(sore, 2, SessionOutcome.Done, Blank());

        Assert.Empty(cleared.OpenFor(Sam));
        Assert.Single(cleared.OpenFor(Lee));
    }

    [Fact]
    public void ABlankAnswersEveryOpenNoteOfThatPerson()
    {
        var sore = Sore("left knee").Note(Sam, "right shoulder", Day(1));
        Assert.Equal(2, sore.OpenFor(Sam).Count);

        Assert.Empty(Settled(sore, 2, SessionOutcome.Done, Blank()).OpenFor(Sam));
    }

    [Fact]
    public void ANewNoteAfterClearingOpensAgain()
    {
        var again = Settled(Sore("left knee"), 2, SessionOutcome.Done, Blank()).Note(Sam, "left knee", Day(5));

        var note = Assert.Single(again.OpenFor(Sam));
        Assert.Equal(Day(5), note.Noted);
        Assert.False(note.DoctorNotePrinted);
    }

    // ---- DoctorOnlyOnce ----

    [Fact]
    public void DoctorOnlyOnce()
    {
        var sore = Sore("left knee", noted: 0);

        Assert.False(sore.PageModel(Sam, Day(5))!.DoctorNoteDue);
        var lasting = sore.PageModel(Sam, Day(6))!;
        Assert.True(lasting.DoctorNoteDue);
        Assert.Equal(7, lasting.DaysSore);

        var printed = sore.MarkDoctorNotePrinted(Sam);

        Assert.False(printed.PageModel(Sam, Day(6))!.DoctorNoteDue);
        Assert.False(printed.PageModel(Sam, Day(20))!.DoctorNoteDue);
        Assert.False(ProgressionLedgerRoundTrip(printed).PageModel(Sam, Day(20))!.DoctorNoteDue);
    }

    private static SorenessLedger ProgressionLedgerRoundTrip(SorenessLedger ledger) => SorenessLedger.FromJson(ledger.ToJson()).Ledger;

    [Fact]
    public void AlarmingSignalsEarnItEarly()
    {
        var sore = Sore("left knee", noted: 1);
        var alarming = new RecoveryRead(RecoveryState.Alarming, [RecoveryReason.ShortSleep, RecoveryReason.RaisedRestingHeartRate], [], []);
        var under = new RecoveryRead(RecoveryState.UnderRecovered, [RecoveryReason.ShortSleep], [], []);

        Assert.True(sore.PageModel(Sam, Day(2), alarming)!.DoctorNoteDue);
        Assert.False(sore.PageModel(Sam, Day(2), under)!.DoctorNoteDue);
        Assert.False(sore.PageModel(Sam, Day(2), RecoveryRead.Nothing)!.DoctorNoteDue);
        Assert.False(sore.PageModel(Sam, Day(2))!.DoctorNoteDue);
    }

    [Fact]
    public void AlarmingSignalsAloneAreNotASoreness()
    {
        var alarming = new RecoveryRead(RecoveryState.Alarming, [RecoveryReason.ShortSleep], [], []);

        Assert.Null(SorenessLedger.Empty.PageModel(Sam, Day(2), alarming));
    }

    [Fact]
    public void PrintedOnceHoldsEvenIfTheSignalsStayAlarming()
    {
        var alarming = new RecoveryRead(RecoveryState.Alarming, [RecoveryReason.ShortSleep], [], []);
        var printed = Sore("left knee").MarkDoctorNotePrinted(Sam);

        Assert.False(printed.PageModel(Sam, Day(3), alarming)!.DoctorNoteDue);
    }

    [Fact]
    public void MarkingOnePersonPrintedLeavesTheOtherDue()
    {
        var sore = Sore("left knee", noted: 0).Note(Lee, "right shoulder", Day(0)).MarkDoctorNotePrinted(Sam);

        Assert.False(sore.PageModel(Sam, Day(8))!.DoctorNoteDue);
        Assert.True(sore.PageModel(Lee, Day(8))!.DoctorNoteDue);
    }

    [Fact]
    public void AnotherNoteInTheSameEpisodeDoesNotPrintItAgain()
    {
        var printed = Sore("left knee", noted: 0).MarkDoctorNotePrinted(Sam).Note(Sam, "right shoulder", Day(7));

        Assert.False(printed.PageModel(Sam, Day(14))!.DoctorNoteDue);
    }

    [Fact]
    public void ANewEpisodeMayEarnItAgain()
    {
        var cleared = Settled(Sore("left knee", noted: 0).MarkDoctorNotePrinted(Sam), 2, SessionOutcome.Done, Blank());
        var again = cleared.Note(Sam, "right shoulder", Day(10));

        Assert.False(again.PageModel(Sam, Day(11))!.DoctorNoteDue);
        Assert.True(again.PageModel(Sam, Day(16))!.DoctorNoteDue);
    }

    // ---- the page ----

    [Fact]
    public void ThePageCarriesThePlaceTheDateAndTheDays()
    {
        var sore = Sore("my left knee", noted: 1);

        var model = sore.PageModel(Sam, Day(3))!;

        Assert.Equal("left knee", model.Place);
        Assert.Equal("Nov 2", model.Since);
        Assert.Equal(3, model.DaysSore);
        Assert.False(model.DoctorNoteDue);
    }

    [Fact]
    public void WithTwoNotesThePageNamesTheOlder()
    {
        var sore = Sore("right shoulder", noted: 2).Note(Sam, "left knee", Day(1));

        Assert.Equal("left knee", sore.PageModel(Sam, Day(3))!.Place);
    }

    [Fact]
    public void NoSorenessNoPageModel()
    {
        Assert.Null(SorenessLedger.Empty.PageModel(Sam, Day(3)));
        Assert.Null(Sore("left knee", person: Lee).PageModel(Sam, Day(3)));
    }

    // ---- notes ----

    [Fact]
    public void AnAreaAlreadyOpenIsNotNotedTwice()
    {
        var sore = Sore("left knee", noted: 1).Note(Sam, "knee again", Day(3));

        var note = Assert.Single(sore.OpenFor(Sam));
        Assert.Equal(Day(1), note.Noted);
        Assert.Equal("left knee", note.Place);
    }

    [Fact]
    public void ADifferentAreaIsAnotherNote()
    {
        var sore = Sore("left knee").Note(Sam, "neck", Day(2));

        Assert.Equal([SoreArea.Knee, SoreArea.Neck], sore.OpenFor(Sam).Select(item => item.Area).ToArray());
    }

    [Fact]
    public void ThePlaceIsTidiedAndKeptShort()
    {
        var sore = Sore("  my   left\tknee  \n");
        Assert.Equal("left knee", Assert.Single(sore.OpenFor(Sam)).Place);

        var long80 = new string('x', 80);
        Assert.Equal(60, Sore(long80).OpenFor(Sam).Single().Place.Length);
    }

    [Fact]
    public void ANoteNeedsSomeoneAndAPlace()
    {
        Assert.Throws<ArgumentException>(() => SorenessLedger.Empty.Note(" ", "knee", Day(1)));
        Assert.Throws<ArgumentException>(() => SorenessLedger.Empty.Note(Sam, " ", Day(1)));
    }

    [Theory]
    [InlineData("my left knee", SoreArea.Knee)]
    [InlineData("Right KNEE", SoreArea.Knee)]
    [InlineData("kneecap", SoreArea.Knee)]
    [InlineData("left shoulder", SoreArea.Shoulder)]
    [InlineData("rotator cuff", SoreArea.Shoulder)]
    [InlineData("neck", SoreArea.Neck)]
    [InlineData("lower back", SoreArea.LowerBack)]
    [InlineData("low back", SoreArea.LowerBack)]
    [InlineData("back", SoreArea.LowerBack)]
    [InlineData("upper back", SoreArea.UpperBack)]
    [InlineData("between my shoulder blades", SoreArea.UpperBack)]
    [InlineData("right hip", SoreArea.Hip)]
    [InlineData("glutes", SoreArea.Hip)]
    [InlineData("hamstring", SoreArea.Thigh)]
    [InlineData("quads", SoreArea.Thigh)]
    [InlineData("left ankle", SoreArea.Foot)]
    [InlineData("heel", SoreArea.Foot)]
    [InlineData("calf", SoreArea.Foot)]
    [InlineData("shin", SoreArea.Foot)]
    [InlineData("right elbow", SoreArea.Arm)]
    [InlineData("wrist", SoreArea.Arm)]
    [InlineData("somewhere in my side", SoreArea.Unknown)]
    [InlineData("", SoreArea.Unknown)]
    [InlineData(null, SoreArea.Unknown)]
    public void ThePlaceWordsNameTheArea(string? place, SoreArea expected)
    {
        Assert.Equal(expected, SoreAreas.Read(place));
    }

    // ---- the stored file ----

    [Fact]
    public void TheLedgerRoundTripsThroughItsFile()
    {
        var ledger = Sore("left knee", noted: 1).Note(Lee, "right shoulder", Day(2)).MarkDoctorNotePrinted(Lee);

        var read = SorenessLedger.FromJson(ledger.ToJson());

        Assert.Equal(2, ledger.Notes.Count);
        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(0, read.UnreadableEntries);
        Assert.Equal(ledger.Notes.ToArray(), read.Ledger.Notes.ToArray());
    }

    [Fact]
    public void TheStoredNamesAreLiteralsAndPinned()
    {
        using var document = JsonDocument.Parse(Sore("left knee").ToJson());
        var root = document.RootElement;

        Assert.Equal(["notes", "version"], root.EnumerateObject().Select(item => item.Name).Order().ToArray());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var note = Assert.Single(root.GetProperty("notes").EnumerateArray());
        Assert.Equal(["area", "doctorNotePrinted", "noted", "person", "place"], note.EnumerateObject().Select(item => item.Name).Order().ToArray());
        Assert.Equal("knee", note.GetProperty("area").GetString());
        Assert.Equal("2026-11-02", note.GetProperty("noted").GetString());
    }

    [Theory]
    [InlineData(SoreArea.Unknown, "unknown")]
    [InlineData(SoreArea.Neck, "neck")]
    [InlineData(SoreArea.Shoulder, "shoulder")]
    [InlineData(SoreArea.Arm, "arm")]
    [InlineData(SoreArea.UpperBack, "upper-back")]
    [InlineData(SoreArea.LowerBack, "lower-back")]
    [InlineData(SoreArea.Hip, "hip")]
    [InlineData(SoreArea.Thigh, "thigh")]
    [InlineData(SoreArea.Knee, "knee")]
    [InlineData(SoreArea.Foot, "foot")]
    public void EveryAreaHasAFrozenStoredName(SoreArea area, string stored)
    {
        var ledger = SorenessLedger.Empty.Note(Sam, SamePlace(area), Day(1));
        Assert.Contains($"\"area\":\"{stored}\"", ledger.ToJson());
        Assert.Equal(area, SorenessLedger.FromJson(ledger.ToJson()).Ledger.OpenFor(Sam).Single().Area);
    }

    private static string SamePlace(SoreArea area) => area switch
    {
        SoreArea.Unknown => "somewhere odd",
        SoreArea.Neck => "neck",
        SoreArea.Shoulder => "shoulder",
        SoreArea.Arm => "elbow",
        SoreArea.UpperBack => "upper back",
        SoreArea.LowerBack => "lower back",
        SoreArea.Hip => "hip",
        SoreArea.Thigh => "thigh",
        SoreArea.Knee => "knee",
        _ => "ankle",
    };

    [Fact]
    public void NothingStoredIsAnEmptyLedgerWithNoProblem()
    {
        foreach (var stored in new string?[] { null, "", " " })
        {
            var read = SorenessLedger.FromJson(stored);
            Assert.False(read.WholeFileUnreadable);
            Assert.Equal(0, read.UnreadableEntries);
            Assert.Empty(read.Ledger.Notes);
        }
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("{\"version\":3,\"notes\":[]}")]
    [InlineData("{\"notes\":[]}")]
    [InlineData("[1]")]
    public void AFileThatIsNotALedgerIsFlaggedNotRead(string stored)
    {
        var read = SorenessLedger.FromJson(stored);

        Assert.True(read.WholeFileUnreadable);
        Assert.Empty(read.Ledger.Notes);
    }

    [Fact]
    public void ABadEntryIsCountedAndTheRestKept()
    {
        var json = "{\"version\":1,\"notes\":["
            + "{\"person\":\"Sam\",\"place\":\"left knee\",\"area\":\"knee\",\"noted\":\"2026-11-03\",\"doctorNotePrinted\":false},"
            + "{\"person\":\"Sam\",\"place\":\"x\",\"area\":\"elbowish\",\"noted\":\"2026-11-03\",\"doctorNotePrinted\":false},"
            + "{\"person\":\"Sam\",\"place\":\"x\",\"area\":\"neck\",\"noted\":\"3 Nov\",\"doctorNotePrinted\":false}]}";

        var read = SorenessLedger.FromJson(json);

        Assert.False(read.WholeFileUnreadable);
        Assert.Equal(2, read.UnreadableEntries);
        Assert.Equal("left knee", Assert.Single(read.Ledger.Notes).Place);
    }

    // ---- a settle that cannot settle ----

    [Fact]
    public void SettlingNeedsSomeoneAndSomeBoxes()
    {
        var sore = Sore("left knee");

        Assert.Throws<ArgumentException>(() => sore.Settle(" ", Day(2), SessionOutcome.Done, Boxes, Blank()));
        Assert.Throws<ArgumentException>(() => sore.Settle(Sam, Day(2), SessionOutcome.Done, [], Blank()));
    }
}
