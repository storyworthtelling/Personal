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

    private readonly IDriveTextFile file;

    public DrivePersonalVaultStore(IDriveTextFile file) => this.file = file;

    public async Task<PersonalVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken)
    {
        var (status, plaintext) = VaultEnvelope.Read(await file.ReadAsync(cancellationToken), passphrase);
        if (status != SecretsVaultStatus.Loaded)
        {
            return new PersonalVaultLoadResult(Map(status), PersonalVault.Empty);
        }

        try
        {
            var vault = JsonSerializer.Deserialize<PersonalVault>(plaintext!, VaultEnvelope.SerializerOptions);
            return vault is null ? PersonalVaultLoadResult.Corrupt : PersonalVaultLoadResult.Loaded(vault);
        }
        catch (JsonException)
        {
            return PersonalVaultLoadResult.Corrupt;
        }
    }

    public Task SaveAsync(PersonalVault vault, string passphrase, CancellationToken cancellationToken) =>
        file.WriteAsync(
            VaultEnvelope.Write(JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions), passphrase),
            cancellationToken);

    // Told apart rather than collapsed: a mistyped passphrase must never read as
    // "your sign-in is gone", or somebody pairs a phone again for no reason.
    private static PersonalVaultStatus Map(SecretsVaultStatus status) => status switch
    {
        SecretsVaultStatus.Empty => PersonalVaultStatus.Empty,
        SecretsVaultStatus.WrongPassphrase => PersonalVaultStatus.WrongPassphrase,
        _ => PersonalVaultStatus.Corrupt,
    };
}
