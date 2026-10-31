namespace Constitution.Infrastructure.Vault;

/// <summary>
/// Where the vault sync says what happened. A phrase is an outcome ("vault restored 3 fields"), never
/// a value, a passphrase or a name, so the Android head can hand it straight to the diagnostics log.
/// </summary>
public interface IConstitutionVaultLog
{
    /// <summary>Records one short outcome phrase.</summary>
    void Outcome(string phrase);
}
