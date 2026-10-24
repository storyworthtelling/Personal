using Android.Content;
using Constitution.Body;
using Constitution.BodyTab;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.ReadBack;
using Constitution.WorkoutDelivery;
using Storyworthtelling.Remarkable;

namespace Constitution.WorkoutWeek;

/// <summary>
/// The delivery-side members of <c>WorkoutWeekSeams</c>, built from the stores and clients the rest of the head already uses.
/// <see cref="Documents"/> is the page-level door over <see cref="Delivery"/> that the run's redelivery calls.
/// </summary>
public sealed record DeliverySideSeams(
    DeliveryService Delivery,
    WorkoutDocumentDelivery Documents,
    IRemarkableClient Tablet,
    IPageInkProbe InkProbe,
    IExpectedDocuments Expected,
    IDeliveredWorkouts Delivered,
    IWorkoutEncouragement Encouragement,
    IWorkoutWeekLog Log,
    IPlanLog PlanLog);

/// <summary>
/// Builds <see cref="DeliverySideSeams"/>. Only wiring; the decisions are in Domain and Infrastructure and are tested there.
///
/// <para>The tablet client and the page reader come from <see cref="ReadBackServices"/>, so the device token and the Claude key
/// each have exactly one path, and the expected-documents file is the read-back's own (one path, one helper), so a page
/// delivered here is waiting where the read-back will look for its ink. Nothing is read from the network here: a missing
/// pairing is found when delivery first uploads, and raises the reMarkable Problem notice, as it does for the read-back. A
/// missing Claude key makes the ink probe answer "unknown", which keeps the page.</para>
///
/// <para>Device-gated: nothing here has met the reMarkable cloud, Claude, or a phone.</para>
/// </summary>
public static class WorkoutWeekDeliverySeams
{
    /// <summary>The seams for this device. Builds objects only; it does no I/O and does not throw for a missing pairing or key.</summary>
    public static DeliverySideSeams Create(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var appContext = context.ApplicationContext ?? context;

        var tablet = ReadBackServices.TabletOf(appContext);
        var expected = ReadBackServices.ExpectedOf(appContext);
        var proof = ReadBackServices.InkProofOf(appContext);
        var probe = new VisionPageInkProbe(
            new RemarkableAnnotatedPageSource(tablet),
            ReadBackServices.VisionOf(appContext),
            expected,
            () => proof.Current.IsEstablished);

        var delivery = new DeliveryService(tablet, new ConnectionNoticeBoardAccess(), () => DateTimeOffset.Now);
        var log = new BodyLog(appContext);
        var weekLog = new BodyWeekLog(log);

        return new DeliverySideSeams(
            delivery,
            new WorkoutDocumentDelivery(delivery, tablet, probe),
            tablet,
            probe,
            expected,
            new FileDeliveredWorkouts(WorkoutWeekPaths.Delivered(NoBackupRoot(appContext))),
            new SeatLineEncouragement((request, cancellation) => BodySeats.ComposeAsync(appContext, request, cancellation)),
            weekLog,
            new TrailPlanLog(weekLog));
    }

    private static string NoBackupRoot(Context context) =>
        context.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep the delivered pages' record in.");

    // The Body trail takes the run's value-free phrases as they are.
    private sealed class BodyWeekLog(BodyLog log) : IWorkoutWeekLog
    {
        public void Outcome(string phrase) => log.Outcome(phrase);
    }
}
