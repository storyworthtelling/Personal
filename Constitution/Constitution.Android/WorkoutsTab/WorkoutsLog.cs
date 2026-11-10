using Android.Content;
using Constitution.Services;

namespace Constitution.WorkoutsTab;

/// <summary>
/// The Workouts tab's outcome trail: a capped file of short phrases in the app's FilesDir, under its own name so a line per
/// open cannot roll a failure history out. A phrase names the act and the exception's type; it never carries a day, a
/// session, a person or an event.
/// </summary>
public sealed class WorkoutsLog : ConstitutionTrail
{
    public const string FileName = "workouts.log";

    /// <summary>The trail's cap; past it the older half is sealed into the log loop's outbox, not discarded.</summary>
    public const long MaxBytes = 32 * 1024;
    private const string Source = "Workouts";

    public WorkoutsLog(Context context)
        : base(context, FileName, MaxBytes, Source)
    {
    }
}
