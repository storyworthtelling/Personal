using Index.Domain.Household;

namespace Index.Infrastructure.Household;

// Mirrors Storyworthtelling.Voice.Secrets.SecretsVaultLoadResult's exact shape — the household file goes
// through the same VaultEnvelope.Read the vault itself uses (see HouseholdSnapshotReader), so a wrong
// passphrase surfaces through the identical WrongPassphraseException path, distinguished from "nothing has
// ever been published" and "the file exists but isn't a valid envelope" the same way the vault already is.
public enum HouseholdSnapshotStatus
{
    // No file exists yet — the owner's install has never published (or her own read is arriving before
    // the owner's first refresh cycle has completed since Index was installed).
    Empty,

    // The file was found and decrypted successfully.
    Loaded,

    // A file exists but the passphrase entered doesn't open it.
    WrongPassphrase,

    // A file exists but isn't a valid envelope, or the plaintext inside it isn't a valid snapshot —
    // not something a passphrase can fix.
    Corrupt,

    // A well-formed envelope whose plaintext declares a "Version" newer than this build understands
    // (remediation 3.6). Not damaged and not a passphrase problem: refused rather than misread, and never
    // overwritten (a publisher that downgraded it would delete what a newer install wrote).
    NewerVersion
}

public sealed record HouseholdSnapshotReadResult(
    HouseholdSnapshotStatus Status,
    HouseholdSnapshot? Snapshot,

    // Set only for NewerVersion: who published the file and which version it declares. Both are read
    // without deserialising the rest, so an older install can still tell whose file it is (the role
    // lookup and the publisher's refusal both need that) while declining to read the body.
    string? NewerFilePublisherEmail = null,
    int? NewerFileVersion = null)
{
    public static HouseholdSnapshotReadResult NewerVersion(int version, string? publisherEmail) =>
        new(HouseholdSnapshotStatus.NewerVersion, null, publisherEmail, version);

    public static HouseholdSnapshotReadResult Empty { get; } = new(HouseholdSnapshotStatus.Empty, null);
    public static HouseholdSnapshotReadResult WrongPassphrase { get; } = new(HouseholdSnapshotStatus.WrongPassphrase, null);
    public static HouseholdSnapshotReadResult Corrupt { get; } = new(HouseholdSnapshotStatus.Corrupt, null);

    public static HouseholdSnapshotReadResult Loaded(HouseholdSnapshot snapshot) =>
        new(HouseholdSnapshotStatus.Loaded, snapshot);
}
