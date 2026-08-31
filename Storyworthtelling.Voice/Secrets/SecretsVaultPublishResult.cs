namespace Storyworthtelling.Voice.Secrets;

public enum SecretsVaultPublishStatus
{
    // The section was merged in and the vault written back successfully.
    Published,

    // No passphrase is set, so this app doesn't participate in the vault yet. Deliberately quiet — the
    // app keeps working from its local settings and simply hasn't opted in.
    NoPassphrase,

    // A vault file exists but couldn't be read (wrong passphrase or a corrupt file). Publishing was refused
    // rather than risk clobbering another app's secrets — so the owner needs to fix the passphrase.
    VaultUnreadable,

    // The vault read fine but writing it back to storage failed (typically no full `drive` scope, or no
    // network). This is the failure that silently left the vault empty after the cutover, so it must surface.
    WriteFailed,

    // This install reads the shared vault and does not write it — the second phone in
    // the household. Not a failure and not something to act on: it is the design.
    ReadOnly,
}

// The outcome of a PublishAsync. Publishing is best-effort — an app always works from its local settings —
// but the two failure states below (VaultUnreadable, WriteFailed) mean the backup silently did NOT happen
// even though a passphrase was set, which is exactly what left the cutover vault empty. Callers surface
// those to the owner instead of swallowing them; NoPassphrase stays quiet.
public sealed record SecretsVaultPublishResult(SecretsVaultPublishStatus Status, string? Error = null)
{
    public static SecretsVaultPublishResult Published { get; } = new(SecretsVaultPublishStatus.Published);
    public static SecretsVaultPublishResult NoPassphrase { get; } = new(SecretsVaultPublishStatus.NoPassphrase);
    public static SecretsVaultPublishResult VaultUnreadable { get; } = new(SecretsVaultPublishStatus.VaultUnreadable);
    public static SecretsVaultPublishResult WriteFailed(string error) => new(SecretsVaultPublishStatus.WriteFailed, error);
    public static SecretsVaultPublishResult ReadOnly { get; } = new(SecretsVaultPublishStatus.ReadOnly);

    // True only when the section was actually stored in the vault.
    public bool Succeeded => Status == SecretsVaultPublishStatus.Published;

    // True for the states the owner should see and act on: a passphrase was set but the backup didn't land.
    // NoPassphrase is not "attention" — the app just isn't in the vault yet, which is a normal starting state.
    // ReadOnly is deliberately absent: her install not writing the household vault is
    // it working, and a settings screen that flagged it would be nagging about a
    // decision nobody can or should change.
    public bool NeedsAttention => Status is SecretsVaultPublishStatus.VaultUnreadable or SecretsVaultPublishStatus.WriteFailed;

    // A neutral, factual one-liner for the settings screen (empty when there's nothing to surface). It always
    // reassures that the local settings are safe — the vault is only the off-device backup.
    public string Message => Status switch
    {
        SecretsVaultPublishStatus.VaultUnreadable =>
            "Couldn't back up to the vault: the passphrase doesn't match the vault already on Drive. " +
            "Your settings are saved on this device; fix the passphrase to sync the backup.",
        SecretsVaultPublishStatus.WriteFailed =>
            $"Couldn't back up to the vault: {Error} " +
            "Your settings are saved on this device. This usually means Drive access needs the full-access " +
            "scope re-granted — sign in again, then Save.",
        _ => string.Empty
    };
}
