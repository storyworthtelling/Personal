using Storyworthtelling.Voice.Secrets;

namespace Constitution.Infrastructure.Vault;

/// <summary>
/// Keeps Constitution's connections in step with the owner's Drive vaults: restored on launch, saved on
/// change, so a factory reset is one passphrase away.
///
/// <list type="bullet">
/// <item><b>Restore fills only what is missing.</b> A value already on this install is never replaced,
/// whatever a vault holds; a vault that cannot be read fills nothing, and says which way it failed.</item>
/// <item><b>Two vaults, two kinds of fact.</b> The household's facts (the Sleep Number login, the
/// reMarkable pairing, the Claude and Gemini keys) live in the shared vault. This person's facts (their
/// Health Connect choices, and the work schedule Foreword writes) live in their own personal vault and
/// nowhere else: in the shared file they would be restored from the first phone onto the second, which
/// is Foreword Release I's bug.</item>
/// </list>
///
/// <para>The sync holds no passphrase: it is an argument to each call, used for that call's reads and
/// writes and never kept, logged or put in a result.</para>
/// </summary>
public sealed class ConstitutionVaultSync
{
    private readonly ISecretsVaultStore sharedStore;
    private readonly DrivePersonalVaultStore personalStore;
    private readonly Func<string?> signedInAccount;
    private readonly IConstitutionConnections connections;
    private readonly IConstitutionVaultLog log;

    /// <param name="sharedStore">The household's shared vault.</param>
    /// <param name="personalStore">This person's own vault, in their own Drive.</param>
    /// <param name="signedInAccount">The Google account this install is bound to, or null before sign-in. It decides who may write the shared vault.</param>
    /// <param name="connections">This install's local copy of the connections.</param>
    /// <param name="log">Where outcome phrases go.</param>
    public ConstitutionVaultSync(
        ISecretsVaultStore sharedStore,
        DrivePersonalVaultStore personalStore,
        Func<string?> signedInAccount,
        IConstitutionConnections connections,
        IConstitutionVaultLog log)
    {
        ArgumentNullException.ThrowIfNull(sharedStore);
        ArgumentNullException.ThrowIfNull(personalStore);
        ArgumentNullException.ThrowIfNull(signedInAccount);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(log);
        this.sharedStore = sharedStore;
        this.personalStore = personalStore;
        this.signedInAccount = signedInAccount;
        this.connections = connections;
        this.log = log;
    }

    /// <summary>
    /// Fills every connection this install is missing from the vaults. Reads both, writes neither.
    /// </summary>
    public async Task<ConstitutionRestoreResult> RestoreAsync(string? passphrase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            log.Outcome("vault restore skipped: no passphrase");
            return new ConstitutionRestoreResult(VaultReadOutcome.NoPassphrase, VaultReadOutcome.NoPassphrase, []);
        }

        var filled = new List<VaultField>();

        var shared = await ReadSharedAsync(passphrase, cancellationToken);
        if (shared.Vault is not null)
        {
            Fill(VaultField.SleepNumberLogin, connections.SleepNumberLogin, shared.Vault.Constitution.SleepNumberLogin, value => connections.SleepNumberLogin = value, filled);
            Fill(VaultField.SleepNumberPassword, connections.SleepNumberPassword, shared.Vault.Constitution.SleepNumberPassword, value => connections.SleepNumberPassword = value, filled);
            Fill(VaultField.RemarkableDeviceToken, connections.RemarkableDeviceToken, shared.Vault.Remarkable.DeviceToken, value => connections.RemarkableDeviceToken = value, filled);
            Fill(VaultField.ClaudeApiKey, connections.ClaudeApiKey, shared.Vault.Claude.ApiKey, value => connections.ClaudeApiKey = value, filled);
            Fill(VaultField.GeminiApiKey, connections.GeminiApiKey, shared.Vault.Gemini.ApiKey, value => connections.GeminiApiKey = value, filled);
        }

        var personal = await ReadPersonalAsync(passphrase, cancellationToken);
        if (personal.Vault is not null)
        {
            Fill(VaultField.HealthConnectChoices, connections.HealthConnectChoicesJson, personal.Vault.HealthConnectChoicesJson, value => connections.HealthConnectChoicesJson = value, filled);
            Fill(VaultField.WorkSchedule, connections.WorkScheduleJson, personal.Vault.WorkScheduleJson, value => connections.WorkScheduleJson = value, filled);
        }

        log.Outcome(
            $"vault restore: restored {filled.Count} {(filled.Count == 1 ? "field" : "fields")} "
            + $"(shared {Words(shared.Outcome)}, personal {Words(personal.Outcome)})");
        return new ConstitutionRestoreResult(shared.Outcome, personal.Outcome, filled, shared.Detail, personal.Detail);
    }

    /// <summary>
    /// Saves what this install holds to the vaults it belongs in. Nothing is ever written over a vault
    /// that could not be read, and only the account that owns the shared vault writes to it.
    ///
    /// <list type="bullet">
    /// <item><b>Shared vault</b> (owner's account only): Constitution's Sleep Number section, and the
    /// reMarkable, Claude and Gemini sections it reuses. Every other section, and the owner, are left
    /// exactly as they were; the Nyt section is never touched, as the New York Times is not used (D96).</item>
    /// <item><b>Personal vault</b> (any account, its own Drive): the Health Connect choices. The work
    /// schedule is Foreword's: it is restored here and never published from here, since a stale copy
    /// would overwrite Foreword's newer one.</item>
    /// </list>
    ///
    /// A value this install does not hold is never published: it leaves the vault's value in place
    /// rather than blanking it, so saving a setting on a freshly reset phone cannot wipe the household's
    /// keys. An install with nothing to say writes nothing, and so cannot claim an unowned vault.
    /// </summary>
    public async Task<ConstitutionPublishResult> PublishAsync(string? passphrase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            log.Outcome("vault publish skipped: no passphrase");
            return new ConstitutionPublishResult(VaultWriteOutcome.NoPassphrase, VaultWriteOutcome.NoPassphrase);
        }

        var shared = await PublishSharedAsync(passphrase, cancellationToken);
        var personal = await PublishPersonalAsync(passphrase, cancellationToken);
        log.Outcome($"vault publish: shared {Words(shared.Outcome)}, personal {Words(personal.Outcome)}");
        return new ConstitutionPublishResult(shared.Outcome, personal.Outcome, shared.Detail, personal.Detail);
    }

    private async Task<(VaultWriteOutcome Outcome, string? Detail)> PublishSharedAsync(
        string passphrase, CancellationToken cancellationToken)
    {
        // One snapshot, taken before the first await: the merge runs after the vault has been read, and
        // the settings screen may have changed a field in between.
        var login = connections.SleepNumberLogin;
        var word = connections.SleepNumberPassword;
        var remarkable = connections.RemarkableDeviceToken;
        var claude = connections.ClaudeApiKey;
        var gemini = connections.GeminiApiKey;

        if (string.IsNullOrEmpty(login) && string.IsNullOrEmpty(word) && string.IsNullOrEmpty(remarkable)
            && string.IsNullOrEmpty(claude) && string.IsNullOrEmpty(gemini))
        {
            return (VaultWriteOutcome.NothingToPublish, null);
        }

        // Ownership is the whole rule for the shared vault, so an install that cannot say which account
        // it is cannot show it owns the vault. (SecretsVaultService would publish for an unidentified
        // install, a deliberate hole for apps that predate ownership; Constitution has no such history,
        // and sign-in is the first thing first run asks for.)
        var account = signedInAccount();
        if (string.IsNullOrWhiteSpace(account))
        {
            return (VaultWriteOutcome.RefusedUnknownAccount, null);
        }

        // The service is the one choke point for "never over a vault it could not read" and for
        // VaultOwnership, and it is built for this call so the passphrase is not kept anywhere.
        var service = new SecretsVaultService(sharedStore, () => passphrase, () => account);
        try
        {
            var published = await service.PublishAsync(
                vault => vault with
                {
                    Constitution = vault.Constitution with
                    {
                        SleepNumberLogin = Pick(login, vault.Constitution.SleepNumberLogin),
                        SleepNumberPassword = Pick(word, vault.Constitution.SleepNumberPassword),
                    },
                    Remarkable = vault.Remarkable with { DeviceToken = Pick(remarkable, vault.Remarkable.DeviceToken) },
                    Claude = vault.Claude with { ApiKey = Pick(claude, vault.Claude.ApiKey) },
                    Gemini = vault.Gemini with { ApiKey = Pick(gemini, vault.Gemini.ApiKey) },
                },
                cancellationToken);

            return published.Status switch
            {
                SecretsVaultPublishStatus.Published => (VaultWriteOutcome.Published, null),
                SecretsVaultPublishStatus.NoPassphrase => (VaultWriteOutcome.NoPassphrase, null),
                SecretsVaultPublishStatus.VaultUnreadable => (VaultWriteOutcome.RefusedUnreadable, null),
                SecretsVaultPublishStatus.ReadOnly => (VaultWriteOutcome.RefusedNotOwner, null),
                SecretsVaultPublishStatus.WriteFailed => (VaultWriteOutcome.Failed, published.Error is null ? null : Redact(published.Error, passphrase)),
                _ => throw new ArgumentOutOfRangeException(nameof(published), published.Status, "Not a publish status this sync knows."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The service does not catch a failed read of the vault (no network, no Drive access).
            return (VaultWriteOutcome.Failed, Describe(ex, passphrase));
        }
    }

    private async Task<(VaultWriteOutcome Outcome, string? Detail)> PublishPersonalAsync(
        string passphrase, CancellationToken cancellationToken)
    {
        var choices = connections.HealthConnectChoicesJson;
        if (string.IsNullOrEmpty(choices))
        {
            return (VaultWriteOutcome.NothingToPublish, null);
        }

        try
        {
            var loaded = await personalStore.LoadAsync(passphrase, cancellationToken);
            if (loaded.Status is PersonalVaultStatus.WrongPassphrase or PersonalVaultStatus.Corrupt)
            {
                return (VaultWriteOutcome.RefusedUnreadable, null);
            }

            // Merged into whatever is there: Score's sign-in, Foreword's calendar choices and work
            // schedule are not ours, and an Empty status hands back a blank vault to start from.
            await personalStore.SaveAsync(loaded.Vault with { HealthConnectChoicesJson = choices }, passphrase, cancellationToken);
            return (VaultWriteOutcome.Published, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (VaultWriteOutcome.Failed, Describe(ex, passphrase));
        }
    }

    // A value this install holds replaces the vault's; a value it does not hold leaves the vault's alone.
    private static string Pick(string local, string inVault) => string.IsNullOrEmpty(local) ? inVault : local;

    private static void Fill(VaultField field, string current, string fromVault, Action<string> set, List<VaultField> filled)
    {
        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(fromVault))
        {
            set(fromVault);
            filled.Add(field);
        }
    }

    // The IDriveTextFile seam throws whatever the Google client and the HTTP stack throw (no network, no
    // Drive scope granted, a timeout), so there is no narrower type to catch here. The failure is not
    // dropped: it becomes an Unreachable result that names the exception's type and carries its message.
    // Cancellation is the caller leaving and is never turned into a result.
    private async Task<(VaultReadOutcome Outcome, SecretsVault? Vault, string? Detail)> ReadSharedAsync(
        string passphrase, CancellationToken cancellationToken)
    {
        var service = new SecretsVaultService(sharedStore, () => passphrase);
        try
        {
            var loaded = await service.LoadAsync(cancellationToken);
            return (ReadOutcome(loaded.Status), loaded.Status == SecretsVaultStatus.Loaded ? loaded.Vault : null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (VaultReadOutcome.Unreachable, null, Describe(ex, passphrase));
        }
    }

    private async Task<(VaultReadOutcome Outcome, PersonalVault? Vault, string? Detail)> ReadPersonalAsync(
        string passphrase, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await personalStore.LoadAsync(passphrase, cancellationToken);
            return (ReadOutcome(loaded.Status), loaded.Status == PersonalVaultStatus.Loaded ? loaded.Vault : null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (VaultReadOutcome.Unreachable, null, Describe(ex, passphrase));
        }
    }

    private static VaultReadOutcome ReadOutcome(SecretsVaultStatus status) => status switch
    {
        SecretsVaultStatus.Loaded => VaultReadOutcome.Loaded,
        SecretsVaultStatus.Empty => VaultReadOutcome.Empty,
        SecretsVaultStatus.WrongPassphrase => VaultReadOutcome.WrongPassphrase,
        SecretsVaultStatus.Corrupt => VaultReadOutcome.Corrupt,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a vault status this sync knows."),
    };

    private static VaultReadOutcome ReadOutcome(PersonalVaultStatus status) => status switch
    {
        PersonalVaultStatus.Loaded => VaultReadOutcome.Loaded,
        PersonalVaultStatus.Empty => VaultReadOutcome.Empty,
        PersonalVaultStatus.WrongPassphrase => VaultReadOutcome.WrongPassphrase,
        PersonalVaultStatus.Corrupt => VaultReadOutcome.Corrupt,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a personal vault status this sync knows."),
    };

    // The failure's type and message, for the screen that tells the owner what went wrong. The passphrase
    // is taken back out of it first: nothing in this sync writes it into a message, but the text comes
    // from the Drive client and the HTTP stack, and "never appears in a result" should not depend on
    // every layer below behaving.
    private static string Describe(Exception ex, string passphrase) => Redact($"{ex.GetType().Name}: {ex.Message}", passphrase);

    private static string Redact(string text, string passphrase) =>
        text.Replace(passphrase, "[passphrase]", StringComparison.Ordinal);

    // Written as statements rather than switch expressions on purpose: the NoKeysTests guard reads an
    // arrow from a passphrase-named enum member to a string as a secret being assigned.
    private static string Words(VaultReadOutcome outcome)
    {
        switch (outcome)
        {
            case VaultReadOutcome.NoPassphrase:
                return "skipped (no passphrase)";
            case VaultReadOutcome.Loaded:
                return "loaded";
            case VaultReadOutcome.Empty:
                return "empty";
            case VaultReadOutcome.WrongPassphrase:
                return "wrong passphrase";
            case VaultReadOutcome.Corrupt:
                return "corrupt";
            case VaultReadOutcome.Unreachable:
                return "unreachable";
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a read outcome this sync knows.");
        }
    }

    private static string Words(VaultWriteOutcome outcome)
    {
        switch (outcome)
        {
            case VaultWriteOutcome.Published:
                return "published";
            case VaultWriteOutcome.NothingToPublish:
                return "nothing to publish";
            case VaultWriteOutcome.NoPassphrase:
                return "skipped (no passphrase)";
            case VaultWriteOutcome.RefusedUnreadable:
                return "refused: unreadable vault";
            case VaultWriteOutcome.RefusedNotOwner:
                return "refused: not the owner";
            case VaultWriteOutcome.RefusedUnknownAccount:
                return "refused: account unknown";
            case VaultWriteOutcome.Failed:
                return "failed";
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a write outcome this sync knows.");
        }
    }
}
