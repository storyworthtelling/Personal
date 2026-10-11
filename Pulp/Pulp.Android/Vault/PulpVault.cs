using Android.App;
using Android.Content;
using Codex.Ui.Voice;
using Pulp.App;
using Pulp.Domain.Core;
using Pulp.Domain.FirstRun;
using Pulp.Infrastructure.Catalogue;
using Storyworthtelling.Voice.Secrets;

namespace Pulp.Vault;

/// <summary>
/// The Vault page's and first run's one way to the encrypted vault in Drive: read it (and have
/// <see cref="PulpServices.RefreshKeysAsync"/> take the keys up), or publish Pulp's section with a
/// merge that touches that section alone. <see cref="SecretsVaultService.PublishAsync"/> refuses to
/// write over a vault it could not read, so a mistyped passphrase never costs another app its keys.
/// Every outcome comes back as a <see cref="VaultRestoreOutcome"/> or <see cref="VaultSaveOutcome"/>,
/// which <see cref="VaultOutcomes"/> turns into a sentence or a dog-ear notice.
/// </summary>
internal sealed class PulpVault
{
    private const string ApplicationName = "Pulp";
    private const string PrefsName = "pulpVault";
    private const string ReplacedPrefix = "replaced:";

    private readonly PulpServices services;
    private readonly VoiceGoogleAccount account = new();

    public PulpVault(PulpServices services) => this.services = services;

    /// <summary>The account sign-in, forwarded so the picker's result can come back through the activity.</summary>
    public Task<string> SignInAsync(Activity activity) => account.SignIn(activity);

    /// <summary>How many of the three keys this process holds now.</summary>
    public int KeysPresent() => KeysPresent(services.Keys.Keys);

    /// <summary>How many of the three keys <paramref name="keys"/> holds.</summary>
    public static int KeysPresent(PulpSecrets keys) =>
        VaultKeys.All.Count(key => !string.IsNullOrEmpty(ValueOf(keys, key.CredentialId)));

    public string ValueOf(string credentialId) => ValueOf(services.Keys.Keys, credentialId);

    public async Task<VaultRestoreOutcome> RestoreAsync(CancellationToken cancellationToken)
    {
        if (!VoiceGoogleAccount.IsSignedIn)
        {
            return VaultRestoreOutcome.NotSignedIn;
        }

        if (!VaultPassphraseStore.IsSet)
        {
            return VaultRestoreOutcome.NoPassphrase;
        }

        SecretsVaultLoadResult result;
        try
        {
            result = await Access().Service.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, cancellationToken))
        {
            services.Diagnostics.Warn("vault", "The vault could not be read from Drive.", exception);
            return VaultRestoreOutcome.Failed;
        }

        var outcome = result.Status switch
        {
            SecretsVaultStatus.Empty => VaultRestoreOutcome.NoVaultYet,
            SecretsVaultStatus.Loaded => VaultRestoreOutcome.Opened,
            SecretsVaultStatus.WrongPassphrase => VaultRestoreOutcome.WrongPassphrase,
            SecretsVaultStatus.Corrupt => VaultRestoreOutcome.Unreadable,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "A vault read with no outcome decided."),
        };

        if (outcome == VaultRestoreOutcome.Opened)
        {
            // The keys arrive by themselves: the services' own source reads the section just opened.
            await services.RefreshKeysAsync(cancellationToken);
        }

        return outcome;
    }

    /// <summary>
    /// Publishes Pulp's section with what the owner typed — a blank entry keeps the key the vault
    /// already holds (<see cref="VaultKeys.Merge"/>) — then has the services take the keys up.
    /// <paramref name="entered"/> is keyed by credential id.
    /// </summary>
    public async Task<VaultSaveOutcome> SaveAsync(IReadOnlyDictionary<string, string> entered, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entered);
        if (!VoiceGoogleAccount.IsSignedIn)
        {
            return VaultSaveOutcome.NotSignedIn;
        }

        if (!VaultPassphraseStore.IsSet)
        {
            return VaultSaveOutcome.NoPassphrase;
        }

        string Typed(string id) => entered.TryGetValue(id, out var value) ? value : string.Empty;

        SecretsVaultPublishResult result;
        try
        {
            result = await Access().Service.PublishAsync(vault => vault with
            {
                Pulp = new PulpSecrets(
                    VaultKeys.Merge(vault.Pulp.StreamingAvailabilityKey, Typed(PulpCredentialIds.StreamingAvailability)),
                    VaultKeys.Merge(vault.Pulp.OmdbKey, Typed(PulpCredentialIds.Omdb)),
                    VaultKeys.Merge(vault.Pulp.YouTubeKey, Typed(PulpCredentialIds.YouTube))),
            }, cancellationToken);
        }
        catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, cancellationToken))
        {
            // The publish's own read of the vault can throw before it reaches its write (no token, no network).
            services.Diagnostics.Warn("vault", "Pulp's keys could not be published to the vault.", exception);
            return VaultSaveOutcome.WriteFailed;
        }

        var outcome = result.Status switch
        {
            SecretsVaultPublishStatus.Published => VaultSaveOutcome.Saved,
            SecretsVaultPublishStatus.NoPassphrase => VaultSaveOutcome.NoPassphrase,
            SecretsVaultPublishStatus.ReadOnly => VaultSaveOutcome.ReadOnly,
            SecretsVaultPublishStatus.VaultUnreadable => VaultSaveOutcome.Unreadable,
            SecretsVaultPublishStatus.WriteFailed => VaultSaveOutcome.WriteFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "A vault publish with no outcome decided."),
        };

        if (outcome == VaultSaveOutcome.WriteFailed)
        {
            services.Diagnostics.Warn("vault", "The vault write failed: " + result.Error);
        }

        if (outcome == VaultSaveOutcome.Saved)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in VaultKeys.All.Where(key => !string.IsNullOrWhiteSpace(Typed(key.CredentialId))))
            {
                MarkReplaced(key.CredentialId, now);
            }

            await services.RefreshKeysAsync(cancellationToken);
        }

        return outcome;
    }

    /// <summary>When the owner last saved a new value for this key, which outranks a rejection recorded before it.</summary>
    public static DateTimeOffset? ReplacedAt(string credentialId)
    {
        var millis = Prefs().GetLong(ReplacedPrefix + credentialId, 0);
        return millis == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(millis);
    }

    private static void MarkReplaced(string credentialId, DateTimeOffset at)
    {
        var editor = Prefs().Edit()!;
        editor.PutLong(ReplacedPrefix + credentialId, at.ToUnixTimeMilliseconds());
        editor.Apply();
    }

    // The signed-in account decides whether this install may write the household vault: the second
    // phone reads it and never writes (VaultOwnership), whichever page it saves from.
    private SecretsVaultAccess Access() =>
        new(account.GetAccessToken, ApplicationName, () => VoiceGoogleAccount.Email);

    private static ISharedPreferences Prefs() =>
        Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    private static string ValueOf(PulpSecrets keys, string credentialId) => credentialId switch
    {
        PulpCredentialIds.StreamingAvailability => keys.StreamingAvailabilityKey,
        PulpCredentialIds.Omdb => keys.OmdbKey,
        PulpCredentialIds.YouTube => keys.YouTubeKey,
        _ => throw new ArgumentOutOfRangeException(nameof(credentialId), credentialId, "Not one of Pulp's keys."),
    };
}
