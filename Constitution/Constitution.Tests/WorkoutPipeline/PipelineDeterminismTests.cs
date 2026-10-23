using System.Security.Cryptography;
using Constitution.Domain.Calendar;
using Constitution.Domain.Documents;
using Constitution.Domain.Equipment;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Documents;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// Two things the run's idempotence stands on, each shown rather than assumed: a page drawn twice is the same bytes (so a
/// fingerprint says "unchanged" truthfully), and the calendar the planner reads must not hold the events the app wrote
/// (so the seam's rule is a real rule, not a nicety).
/// </summary>
public class PipelineDeterminismTests
{
    private static WorkoutDocumentModel Model(string line = "15 lb today, up from 10.")
    {
        var session = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false,
            [new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId), new Exercise("Wall angels")]);
        var planned = new PlannedSession(Day(0), At(0, 16), new FreeGap(At(0, 16), At(0, 18)), session, [Sam], Together.None);
        var doses = ProgressionPlan.Prescribe(ProgressionLedger.Empty, Sam, session);
        return WorkoutDocumentBuilder.Build(planned, planned.Start, Zone, doses, [line, null]);
    }

    private static byte[] Render(WorkoutDocumentModel model) => ConstitutionDocumentRenderer.Render(WorkoutDocument.Build(model));

    [Fact]
    public void APageDrawnTwiceIsTheSameBytes()
    {
        Assert.Equal(Render(Model()), Render(Model()));
    }

    [Fact]
    public void FingerprintIsSha256Hex()
    {
        var fingerprint = DocumentFingerprint.Of(Render(Model()));

        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
        Assert.Equal(fingerprint, DocumentFingerprint.Of(Render(Model())));
        Assert.Equal(Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant(), DocumentFingerprint.Of([]));
    }

    [Fact]
    public void ADifferentPageHasADifferentFingerprint()
    {
        var model = Model();
        var other = model with { TotalMinutes = model.TotalMinutes + 5 };

        Assert.NotEqual(DocumentFingerprint.Of(Render(model)), DocumentFingerprint.Of(Render(other)));
    }

    [Fact]
    public void OnlyTheVoicedLineIsStripped()
    {
        var voiced = Model("A line the owner would write.");
        var plain = Model("A different line.");

        Assert.NotEqual(DocumentFingerprint.Of(Render(voiced)), DocumentFingerprint.Of(Render(plain)));
        Assert.Equal(
            DocumentFingerprint.Of(Render(WorkoutDocumentBuilder.WithoutVoice(voiced))),
            DocumentFingerprint.Of(Render(WorkoutDocumentBuilder.WithoutVoice(plain))));
        Assert.All(WorkoutDocumentBuilder.WithoutVoice(voiced).Exercises, page => Assert.Null(page.Encouragement));
    }

    [Fact]
    public void OwnEventsLeftInChangeThePlan()
    {
        // The seam must drop events carrying the constitutionKey property. This is why: read as busy time they move the plan.
        var empty = Calendars();
        var first = Plan(empty);
        var placed = first.Plans.SelectMany(week => week.Sessions.Select(session => (week.Person, session))).ToList();
        Assert.NotEmpty(placed);
        var ours = placed.Where(item => item.Person == Sam).Select(item => CalendarEntry.Timed("Workout · 40 min", item.session.Start, item.session.End)).ToList();

        var second = Plan(Calendars(sam: ours));

        Assert.NotEqual(Describe(first), Describe(second));
    }

    private static string Describe(HouseholdWorkoutPlan plan) => string.Join(
        "\n", plan.Plans.SelectMany(week => week.Sessions.Select(session => $"{week.Person}|{session.Start:O}|{session.Session.Kind}")));
}
