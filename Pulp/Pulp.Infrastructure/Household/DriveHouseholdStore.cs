using Pulp.Domain;
using Pulp.Domain.Household;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Pulp.Infrastructure.Household;

/// <summary>
/// The household file in Drive exists and this phone cannot open it — a wrong or missing passphrase,
/// damage, or a newer Pulp's format. Thrown instead of reading it as empty, and before any write: the
/// file holds the other phone's work, and writing this phone's household over it would erase that with
/// nothing anywhere saying so (G14, the prayer-archive rule). The screens turn it into the household
/// notice (<see cref="HouseholdSyncStatus.Notice"/>).
/// </summary>
public sealed class HouseholdUnreadableException : Exception
{
    public HouseholdUnreadableException(HouseholdUnreadableReason reason, string message, Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
    }

    public HouseholdUnreadableReason Reason { get; }
}

/// <summary>A save had nowhere to land: no household file is visible to this account and it may not create one.</summary>
public sealed class HouseholdNotSharedException(string message) : Exception(message);

/// <summary>How a publish pass ended, and the household the phone should hold from now on.</summary>
public sealed record HouseholdPublishResult(HouseholdDocument Held, HouseholdPublishOutcome Outcome, int Writes);

/// <summary>
/// The shared household (finding §2.7): <see cref="PulpIdentity.HouseholdFileName"/> in the owner's
/// Drive, shared to the other adult, encrypted with <see cref="VaultEnvelope"/> under the vault
/// passphrase both adults hold.
///
/// <para><see cref="PublishAsync"/> is the read → merge → write → re-read cycle, its decisions
/// <see cref="HouseholdPublish"/>'s. Every read that precedes a write must open the file: one that
/// cannot throws <see cref="HouseholdUnreadableException"/> and nothing is written (G14). An absent file
/// is created only when <c>mayCreate</c> says this account owns the household; the other adult's phone
/// waits for the owner to share it rather than starting a second household in its own Drive.</para>
///
/// <para>Drive being unreachable is not this class's to decide about: the transport's exception
/// propagates unchanged, so the caller can tell "try again later" from "this file is locked to me".</para>
/// </summary>
public sealed class DriveHouseholdStore : IHouseholdStore
{
    /// <summary>The address, not a description: both phones find the file by it.</summary>
    public const string FileName = PulpIdentity.HouseholdFileName;

    private readonly IDriveTextFile file;
    private readonly Func<string> passphrase;
    private readonly Func<bool> mayCreate;

    public DriveHouseholdStore(IDriveTextFile file, Func<string> passphrase, Func<bool> mayCreate)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(mayCreate);
        this.file = file;
        this.passphrase = passphrase;
        this.mayCreate = mayCreate;
    }

    /// <summary>The household in Drive; empty when there is no file. A file that will not open throws.</summary>
    public async Task<HouseholdDocument> LoadAsync(CancellationToken cancellationToken) =>
        (await ReadAsync(Passphrase(), cancellationToken)).Document;

    /// <summary>Publishes <paramref name="document"/> merged with what Drive holds; another writer's records are never dropped.</summary>
    public async Task SaveAsync(HouseholdDocument document, CancellationToken cancellationToken)
    {
        var result = await PublishAsync(document, cancellationToken);
        if (result.Outcome == HouseholdPublishOutcome.NotYetShared)
        {
            throw new HouseholdNotSharedException(
                $"The household was not saved to Drive: no '{FileName}' is visible to this account, and only the owner's account creates it. The owner shares it once from their Drive.");
        }
    }

    /// <summary>One pass: pull, merge, push if Drive is missing something, re-read, and repair a lost write once.</summary>
    public async Task<HouseholdPublishResult> PublishAsync(HouseholdDocument local, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(local);
        var key = Passphrase();
        var read = await ReadAsync(key, cancellationToken);
        var merged = HouseholdPublish.FirstWrite(local, read.Document);
        if (!HouseholdSync.NeedsWrite(merged, read.Document))
        {
            return new HouseholdPublishResult(merged, HouseholdPublishOutcome.AlreadyCurrent, 0);
        }

        if (!read.Exists && !mayCreate())
        {
            return new HouseholdPublishResult(merged, HouseholdPublishOutcome.NotYetShared, 0);
        }

        var writes = 0;
        while (true)
        {
            await file.WriteAsync(VaultEnvelope.Write(HouseholdDocumentCodec.Serialize(merged), key), cancellationToken);
            writes++;

            var reread = await ReadAsync(key, cancellationToken);
            var step = HouseholdPublish.AfterReread(merged, reread.Document, writes);
            merged = step.Document;
            switch (step.Action)
            {
                case PublishAction.Done:
                    return new HouseholdPublishResult(merged, HouseholdPublishOutcome.Written, writes);
                case PublishAction.LeaveForNextPass:
                    return new HouseholdPublishResult(merged, HouseholdPublishOutcome.LeftForNextPass, writes);
                case PublishAction.WriteAgain:
                    continue;
                default:
                    throw new InvalidOperationException($"The household publish plan returned {step.Action}, which this store does not know how to carry out.");
            }
        }
    }

    private string Passphrase()
    {
        var key = passphrase();
        return string.IsNullOrEmpty(key)
            ? throw new HouseholdUnreadableException(
                HouseholdUnreadableReason.NoPassphrase,
                $"The household file in Drive was neither read nor written: there is no vault passphrase on this phone, and '{FileName}' is encrypted under it.")
            : key;
    }

    private async Task<(bool Exists, HouseholdDocument Document)> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
        var (status, plaintext) = VaultEnvelope.Read(content, key);
        switch (status)
        {
            case SecretsVaultStatus.Empty:
                return (false, HouseholdDocument.Empty);
            case SecretsVaultStatus.WrongPassphrase:
                throw new HouseholdUnreadableException(
                    HouseholdUnreadableReason.WrongPassphrase,
                    $"'{FileName}' in Drive does not open with this phone's vault passphrase; it is left exactly as it is.");
            case SecretsVaultStatus.Loaded:
                break;
            default:
                throw new HouseholdUnreadableException(
                    HouseholdUnreadableReason.Damaged,
                    $"'{FileName}' in Drive is not a readable encrypted file ({status}); it is left exactly as it is.");
        }

        try
        {
            return (true, HouseholdDocumentCodec.Deserialize(plaintext!));
        }
        catch (HouseholdFormatException exception)
        {
            throw new HouseholdUnreadableException(
                exception.IsNewerFormat ? HouseholdUnreadableReason.NewerFormat : HouseholdUnreadableReason.Damaged,
                $"'{FileName}' in Drive opened but could not be read as a household, so it is left exactly as it is: {exception.Message}",
                exception);
        }
    }
}
