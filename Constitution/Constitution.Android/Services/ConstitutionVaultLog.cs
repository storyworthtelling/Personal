using Android.Content;
using Constitution.Infrastructure.Vault;

namespace Constitution.Services;

/// <summary>
/// The vault sync's outcome trail: a capped file of short phrases ("vault restore: restored 3 fields")
/// in the app's FilesDir, in the repo's own <see cref="DiagnosticLog"/> under its own file name so a
/// line per launch cannot roll a failure history out. The sync hands over phrases that carry no value,
/// passphrase or name, and nothing else is ever written here.
/// </summary>
public sealed class ConstitutionVaultLog : ConstitutionTrail, IConstitutionVaultLog
{
    /// <summary>The trail's file name, beside the failure log.</summary>
    public const string FileName = "vault.log";

    /// <summary>The trail's cap; past it the older half is sealed into the log loop's outbox, not discarded.</summary>
    public const long MaxBytes = 32 * 1024;
    private const string Source = "Vault";

    public ConstitutionVaultLog(Context context)
        : base(context, FileName, MaxBytes, Source)
    {
    }
}
