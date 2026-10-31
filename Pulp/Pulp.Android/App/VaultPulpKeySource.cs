using Codex.Ui.Voice;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Infrastructure.Catalogue;
using Storyworthtelling.Voice.Secrets;

namespace Pulp.App;

/// <summary>
/// Pulp's three keys, read from the vault's Pulp section through the FocusPhone heads' own vault
/// access (<see cref="SecretsVaultAccess"/> + <see cref="VaultPassphraseStore"/> +
/// <see cref="VoiceGoogleAccount"/>). Until a refresh finds them — no passphrase, no Google account,
/// or no vault yet — the keys are empty and every client takes its MissingKey path, which is how a
/// fresh install behaves until Release D's first run and Vault screen.
/// </summary>
public sealed class VaultPulpKeySource : IPulpKeySource
{
    private readonly VoiceGoogleAccount account;
    private readonly PulpDiagnostics diagnostics;
    private PulpSecrets keys = new();
    private int householdRole = (int)HouseholdFileRole.Unknown;

    public VaultPulpKeySource(VoiceGoogleAccount account, PulpDiagnostics diagnostics)
    {
        this.account = account;
        this.diagnostics = diagnostics;
    }

    public PulpSecrets Keys => Volatile.Read(ref keys);

    /// <summary>
    /// Whether this phone's account owns the household file or has it shared to it, read off the
    /// vault file's Drive ownership (<see cref="VaultOwnership"/>) on each refresh — the same rule
    /// the vault keeps, so the two never disagree about whose Drive holds the household. <see cref="HouseholdFileRole.Unknown"/> until a refresh reads the vault, which
    /// creates nothing.
    /// </summary>
    public HouseholdFileRole HouseholdRole => (HouseholdFileRole)Volatile.Read(ref householdRole);

    /// <summary>Reads the vault's Pulp section. Never throws for the vault's sake (a timeout included): a phone that cannot reach the vault keeps whatever keys it has. Only the caller cancelling its own token propagates.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!VaultPassphraseStore.IsSet || !VoiceGoogleAccount.IsSignedIn)
        {
            return;
        }

        try
        {
            var vault = new SecretsVaultAccess(account.GetAccessToken, "Pulp");
            var result = await vault.Service.LoadAsync(cancellationToken);
            if (result.Status != SecretsVaultStatus.Loaded)
            {
                diagnostics.Warn("vault", $"The vault was not read ({result.Status}); Pulp's keys stay as they were.");
                return;
            }

            SharedDriveFileIds.SeedFrom(result.Vault.DriveFileIds);
            Volatile.Write(ref keys, result.Vault.Pulp);
            Volatile.Write(ref householdRole, (int)RoleOf(VaultOwnership.Decide(result.FileOwnership)));
        }
        catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, cancellationToken))
        {
            // Restore is a convenience; the clients' MissingKey path says what is missing.
            diagnostics.Warn("vault", "Pulp's keys could not be restored from the vault.", exception);
        }
    }

    // A vault nobody has claimed yet is claimed by the first account to use it, so that account is
    // the household's owner too; a phone with no account signed in has no Drive to own anything in.
    private static HouseholdFileRole RoleOf(SharedVaultAccess access) => access switch
    {
        SharedVaultAccess.ReadWrite or SharedVaultAccess.Claimable => HouseholdFileRole.Owner,
        SharedVaultAccess.ReadOnly => HouseholdFileRole.SharedWith,
        _ => HouseholdFileRole.Unknown,
    };
}
