namespace Constitution.Infrastructure.Vault;

/// <summary>How a runner call ended. Carries no value and no passphrase.</summary>
public enum VaultRunStatus
{
    /// <summary>No vault passphrase has been entered, so Drive was not touched.</summary>
    NoPassphrase,

    /// <summary>No Google account is signed in on this install, so Drive was not touched.</summary>
    NotSignedIn,

    /// <summary>A restore ran; see <see cref="ConstitutionVaultRunResult.Restore"/> for how each vault went.</summary>
    Restored,

    /// <summary>A publish ran; see <see cref="ConstitutionVaultRunResult.Publish"/> for how each vault went.</summary>
    Published,

    /// <summary>The run itself failed (not a vault outcome); <see cref="ConstitutionVaultRunResult.Detail"/> names the failure's type.</summary>
    Failed,
}

/// <summary>What a runner call did.</summary>
/// <param name="Status">How the call ended.</param>
/// <param name="Restore">How a restore went, when one ran.</param>
/// <param name="Publish">How a publish went, when one ran.</param>
/// <param name="Detail">When the run itself failed: the failure's type name, and nothing else.</param>
public sealed record ConstitutionVaultRunResult(
    VaultRunStatus Status,
    ConstitutionRestoreResult? Restore = null,
    ConstitutionPublishResult? Publish = null,
    string? Detail = null);

/// <summary>
/// Runs <see cref="ConstitutionVaultSync"/> the way the app needs it run: reads the passphrase and the
/// sign-in when the call is made (never earlier, never kept), skips quietly when either is missing, and
/// never throws to its caller: every ending is a <see cref="ConstitutionVaultRunResult"/>.
///
/// <para>The seams are constructor arguments so the orchestration reads, and is tested, on its own; the
/// Android head wires them to the shared passphrase store, its Google account and Drive.</para>
/// </summary>
public sealed class ConstitutionVaultRunner
{
    private readonly Func<string?> passphrase;
    private readonly Func<bool> signedIn;
    private readonly Func<ConstitutionVaultSync> syncFactory;
    private readonly IConstitutionVaultLog log;

    public ConstitutionVaultRunner(
        Func<string?> passphrase,
        Func<bool> signedIn,
        Func<ConstitutionVaultSync> syncFactory,
        IConstitutionVaultLog log)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(signedIn);
        ArgumentNullException.ThrowIfNull(syncFactory);
        ArgumentNullException.ThrowIfNull(log);
        this.passphrase = passphrase;
        this.signedIn = signedIn;
        this.syncFactory = syncFactory;
        this.log = log;
    }

    /// <summary>Fills what this install is missing from the vaults. Runs on a thread-pool thread.</summary>
    public Task<ConstitutionVaultRunResult> RestoreAsync(CancellationToken cancellationToken) =>
        Task.Run(() => RunRestoreAsync(cancellationToken), cancellationToken);

    /// <summary>Saves what this install changed to the vaults it belongs in. Runs on a thread-pool thread.</summary>
    /// <param name="changed">The fields just changed here; see <see cref="ConstitutionVaultSync.PublishAsync"/>.</param>
    /// <param name="cancellationToken">The caller leaving.</param>
    public Task<ConstitutionVaultRunResult> PublishAsync(IReadOnlyCollection<VaultField> changed, CancellationToken cancellationToken) =>
        Task.Run(() => RunPublishAsync(changed, cancellationToken), cancellationToken);

    /// <summary>
    /// Restores, then publishes: what a changed passphrase calls. On a phone that was just reset the
    /// order is launch (no passphrase), sign-in, passphrase last, so the passphrase is the moment the
    /// vault can finally be read: it must bring the household's connections back, and only then say what
    /// this install has to add. Restore comes first so that a value it brings back is in place before
    /// anything is written. If the restore could not run (no passphrase, no sign-in, a failure), neither
    /// does the publish, and the result is the restore's.
    /// </summary>
    /// <param name="changed">The fields just changed here (none, for a passphrase alone).</param>
    /// <param name="cancellationToken">The caller leaving.</param>
    /// <returns>The publish's result carrying the restore's too, or the restore's own when it stopped the run.</returns>
    public Task<ConstitutionVaultRunResult> RestoreThenPublishAsync(IReadOnlyCollection<VaultField> changed, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var restored = await RunRestoreAsync(cancellationToken);
            if (restored.Status != VaultRunStatus.Restored)
            {
                return restored;
            }

            var published = await RunPublishAsync(changed, cancellationToken);
            return published with { Restore = restored.Restore };
        }, cancellationToken);

    private async Task<ConstitutionVaultRunResult> RunRestoreAsync(CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
        {
            return new ConstitutionVaultRunResult(VaultRunStatus.NoPassphrase);
        }

        if (!signedIn())
        {
            log.Outcome("vault restore skipped: not signed in");
            return new ConstitutionVaultRunResult(VaultRunStatus.NotSignedIn);
        }

        try
        {
            var result = await syncFactory().RestoreAsync(pass, cancellationToken);
            return new ConstitutionVaultRunResult(VaultRunStatus.Restored, Restore: result);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Failed("restore", e);
        }
    }

    private async Task<ConstitutionVaultRunResult> RunPublishAsync(IReadOnlyCollection<VaultField> changed, CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
        {
            return new ConstitutionVaultRunResult(VaultRunStatus.NoPassphrase);
        }

        if (!signedIn())
        {
            log.Outcome("vault publish skipped: not signed in");
            return new ConstitutionVaultRunResult(VaultRunStatus.NotSignedIn);
        }

        try
        {
            var result = await syncFactory().PublishAsync(pass, changed, cancellationToken);
            return new ConstitutionVaultRunResult(VaultRunStatus.Published, Publish: result);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Failed("publish", e);
        }
    }

    // The type name only: a message from a Drive or crypto failure is not ours to vouch for as secret-free.
    private ConstitutionVaultRunResult Failed(string verb, Exception e)
    {
        var type = e.GetType().Name;
        log.Outcome($"vault {verb} failed: {type}");
        return new ConstitutionVaultRunResult(VaultRunStatus.Failed, Detail: type);
    }
}
