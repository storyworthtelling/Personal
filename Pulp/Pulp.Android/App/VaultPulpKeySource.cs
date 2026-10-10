using Codex.Ui.Voice;
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

    public VaultPulpKeySource(VoiceGoogleAccount account, PulpDiagnostics diagnostics)
    {
        this.account = account;
        this.diagnostics = diagnostics;
    }

    public PulpSecrets Keys => Volatile.Read(ref keys);

    /// <summary>Reads the vault's Pulp section. Never throws: a phone that cannot reach the vault keeps whatever keys it has.</summary>
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
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Restore is a convenience; the clients' MissingKey path says what is missing.
            diagnostics.Warn("vault", "Pulp's keys could not be restored from the vault.", exception);
        }
    }
}
