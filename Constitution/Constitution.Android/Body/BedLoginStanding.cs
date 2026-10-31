using Android.Content;
using Constitution.Domain.Credentials;
using Constitution.Domain.Notices;
using Constitution.Infrastructure.SleepNumber;
using Constitution.Services;

namespace Constitution.Body;

/// <summary>
/// Whether the bed has already refused the login this install holds (review S1). The credential-health report
/// already records, on disk, that a call with the login was rejected and when; judged against the moment the login
/// was last saved by hand (<see cref="ConstitutionHealthReport.HealthOf"/>), that is exactly "refused, and not replaced
/// since". So a changed household password costs one failed sign-in, not one a night every pass, until somebody
/// saves a login again in Settings (which records the new save time and so lifts the memory).
/// </summary>
public sealed class BedLoginStanding(Context context) : ISignInStanding
{
    public bool LoginRefused =>
        ConstitutionHealthReport.HealthOf(
            ConstitutionHealthReport.Read(),
            CredentialIds.SleepNumber,
            new ConstitutionCredentialSaves(context).SavedAt(CredentialIds.SleepNumber)) == ConnectionHealth.Rejected;
}
