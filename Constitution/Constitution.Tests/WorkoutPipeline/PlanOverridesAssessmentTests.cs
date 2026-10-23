using Constitution.Domain.Delivery;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.Documents;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>A first-week session the owner moves is still that assessment test: its page is the test's page on the day it went to.</summary>
public sealed class PlanOverridesAssessmentTests : IDisposable
{
    private readonly WeekRig rig = new();

    public void Dispose() => rig.Dispose();

    private static string TextOf(byte[] pdf) =>
        string.Join(" ", PdfReadBack.Open(pdf).GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));

    [Fact]
    public async Task AMovedTestIsStillATestPage()
    {
        var morning = Est(11, 2, 5, 30);
        var first = await rig.PlannedAsync(rig.Request(morning, Sam));
        var snapshot = OwnerHand.TabShows(rig, first, Day(0), morning);
        var session = first.PlacedPlan.PlanFor(Sam)!.Sessions.First();
        var card = SwapChoices.For(snapshot, session.Day);
        var target = card.Rows.First(row => row.IsEnabled && snapshot.SessionOn(row.Day) is null).Day;
        var (_, overrides) = await OwnerHand.Swaps(rig, snapshot, session.Day, target);

        var report = await rig.PlannedAsync(rig.Request(Est(target.Month, target.Day, 5, 30), Sam) with { PlanOverrides = overrides });

        var outcome = Assert.Single(report.Documents, item => item.Day == target);
        Assert.Equal(DocumentVerdict.Delivered, outcome.Verdict);
        Assert.Contains("Where you start", TextOf(rig.Tablet.PdfOf(DeliveredName.For(DeliveryKind.Workout, target, Sam))), StringComparison.Ordinal);
    }
}
