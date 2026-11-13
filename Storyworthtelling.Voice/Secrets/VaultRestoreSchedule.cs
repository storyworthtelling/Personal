using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Voice.Secrets;

/// <summary>
/// Whether a launch should restore from the vault (efficiency audit §24, owner's decision): at most every six hours,
/// but at once when a value this app restores is missing on the phone or one of its keys was refused, since that is
/// when a newer value in the vault matters. A clock that has gone backwards restores, rather than waiting.
/// </summary>
public static class VaultRestoreSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <param name="lastRestoredUtc">When a launch restore last opened the vault, or null for never.</param>
    /// <param name="localValues">What this app restores, as it holds it now; a blank one is missing.</param>
    /// <param name="health">This app's credential verdicts; a Failing one wants the vault's value.</param>
    public static bool IsDue(DateTimeOffset? lastRestoredUtc, DateTimeOffset now, IEnumerable<string?> localValues, CredentialHealthReport health)
    {
        if (lastRestoredUtc is not { } last || now < last || now - last >= Interval)
        {
            return true;
        }
        return localValues.Any(string.IsNullOrWhiteSpace)
            || health.Ids.Any(id => health.Get(id).Status == CredentialStatus.Failing);
    }
}
