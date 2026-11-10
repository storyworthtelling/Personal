using Android.Content;
using Constitution.Services;
using Constitution.Domain.Body;
using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Body;

/// <summary>
/// Body's outcome trail: a capped file of short phrases ("body read weight: ok, 2 readings") in the app's FilesDir, in the
/// repo's own <see cref="DiagnosticLog"/> under its own file name so a line per launch cannot roll a failure history out.
/// Platform outcomes land here as the owner's later device test will need them: permission state, record counts and
/// exception type names. A phrase carries no reading, no package name and no person's name, and nothing else is written.
/// It also takes the bed's phrases, which have the same rule.
/// </summary>
public sealed class BodyLog : ConstitutionTrail, IBodyLog, ISleepIqLog
{
    /// <summary>The trail's file name, beside the failure log and the vault trail.</summary>
    public const string FileName = "body.log";

    /// <summary>The trail's cap; past it the older half is sealed into the log loop's outbox, not discarded.</summary>
    public const long MaxBytes = 32 * 1024;
    private const string Source = "Body";

    public BodyLog(Context context)
        : base(context, FileName, MaxBytes, Source)
    {
    }
}
