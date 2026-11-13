using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Maui.Health;

/// <summary>
/// A MAUI head's vault restore at launch, at most every six hours unless something is missing or refused
/// (<see cref="VaultRestoreSchedule"/>; efficiency audit §24). The time is kept only when the restore opened the
/// vault, so a launch with no passphrase, no network or no vault yet tries again next time.
/// </summary>
public static class LaunchRestore
{
    private const string RestoredAtKey = "vaultRestoredAtUtc";

    /// <param name="localValues">The values this head restores, as it holds them now.</param>
    /// <param name="restore">The head's restore; true when it opened the vault.</param>
    public static async Task RunAsync(Func<IEnumerable<string?>> localValues, Func<CancellationToken, Task<bool>> restore, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!VaultRestoreSchedule.IsDue(LastRestored(), now, localValues(), MauiCredentialHealth.Store().Load()))
        {
            return;
        }

        if (await restore(cancellationToken))
        {
            Preferences.Default.Set(RestoredAtKey, now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static DateTimeOffset? LastRestored() =>
        DateTimeOffset.TryParse(Preferences.Default.Get(RestoredAtKey, string.Empty), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;
}
