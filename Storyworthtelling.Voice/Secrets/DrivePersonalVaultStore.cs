using System.Text.Json;

namespace Storyworthtelling.Voice.Secrets;

/// <summary>The four ways loading a personal vault can go, matching the shared one.</summary>
public enum PersonalVaultStatus
{
    Empty,
    Loaded,
    WrongPassphrase,
    Corrupt,
}

/// <summary>The outcome of a load. Vault is meaningful only when Status is Loaded.</summary>
public sealed record PersonalVaultLoadResult(PersonalVaultStatus Status, PersonalVault Vault)
{
    /// <summary>The read this result came from, which a save writes over.</summary>
    public SealedRead? Basis { get; init; }

    public static PersonalVaultLoadResult Empty { get; } = new(PersonalVaultStatus.Empty, PersonalVault.Empty);

    public static PersonalVaultLoadResult Corrupt { get; } = new(PersonalVaultStatus.Corrupt, PersonalVault.Empty);

    public static PersonalVaultLoadResult Loaded(PersonalVault vault) => new(PersonalVaultStatus.Loaded, vault);
}

/// <summary>
/// One person's own vault: an encrypted JSON file in <b>their</b> Drive, holding the
/// credentials that are theirs rather than the household's.
///
/// Same envelope, same crypto, same passphrase as the shared vault — deliberately, so
/// there is one thing to understand and one thing to get right. What differs is only
/// whose Drive it lives in and who may write it: the shared vault has a single owner
/// and everyone else reads it, while this one is written by exactly the person it
/// belongs to and never read by anybody else.
///
/// The file name is the same in both people's Drives, because it is found by name and
/// two people's Drives never meet. That is also why nothing here needs to know whose
/// vault it is: the account it was opened with already decided.
/// </summary>
public sealed class DrivePersonalVaultStore
{
    public const string FileName = "storyworthtelling-personal.json";

    private readonly SealedDriveFile sealedFile;

    public DrivePersonalVaultStore(IDriveTextFile file) => sealedFile = new SealedDriveFile(file);

    public async Task<PersonalVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken)
    {
        var read = await sealedFile.ReadAsync(passphrase, cancellationToken);
        read.ThrowIfUnreachable();
        if (read.Status != SealedReadStatus.Loaded)
        {
            return new PersonalVaultLoadResult(Map(read.Status), PersonalVault.Empty) { Basis = read };
        }

        try
        {
            var vault = JsonSerializer.Deserialize<PersonalVault>(read.Plaintext!, VaultEnvelope.SerializerOptions);
            return vault is null
                ? PersonalVaultLoadResult.Corrupt with { Basis = read with { Status = SealedReadStatus.Corrupt } }
                : PersonalVaultLoadResult.Loaded(vault) with { Basis = read };
        }
        catch (JsonException)
        {
            return PersonalVaultLoadResult.Corrupt with { Basis = read with { Status = SealedReadStatus.Corrupt } };
        }
    }

    /// <summary>Writes over the file as it is now, under the sealed-file rule (G14).</summary>
    public async Task SaveAsync(PersonalVault vault, string passphrase, CancellationToken cancellationToken) =>
        await WriteOverAsync(vault, passphrase, await sealedFile.ReadAsync(passphrase, cancellationToken), cancellationToken);

    /// <summary>Writes over the file exactly as <paramref name="basis"/> read it; a later write is a conflict.</summary>
    public Task SaveAsync(PersonalVault vault, string passphrase, PersonalVaultLoadResult basis, CancellationToken cancellationToken) =>
        basis.Basis is { } read
            ? WriteOverAsync(vault, passphrase, read, cancellationToken)
            : SaveAsync(vault, passphrase, cancellationToken);

    private async Task WriteOverAsync(PersonalVault vault, string passphrase, SealedRead basis, CancellationToken cancellationToken)
    {
        var result = await sealedFile.WriteAsync(
            JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions), passphrase, basis, SealedWriteOptions.Default, cancellationToken);
        if (!result.Succeeded)
        {
            throw new VaultWriteException(result);
        }
    }

    // Told apart rather than collapsed: a mistyped passphrase must never read as
    // "your sign-in is gone", or somebody pairs a phone again for no reason.
    private static PersonalVaultStatus Map(SealedReadStatus status) => status switch
    {
        SealedReadStatus.Absent => PersonalVaultStatus.Empty,
        SealedReadStatus.WrongPassphrase => PersonalVaultStatus.WrongPassphrase,
        _ => PersonalVaultStatus.Corrupt,
    };
}
