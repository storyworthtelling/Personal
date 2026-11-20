using Storyworthtelling.Household;

namespace Storyworthtelling.People;

/// <summary>Where the household file stands on this phone.</summary>
public enum PeopleFileState
{
    /// <summary>No pass has run yet.</summary>
    NotCompared,

    /// <summary>No passphrase or no Google account here, so the file can't be reached; the phone's copy is all there is.</summary>
    LocalOnly,

    /// <summary>No household file is visible to this account, and only the owner's phone creates it.</summary>
    WaitingForShare,

    Synced,

    /// <summary>Drive couldn't be reached; the next pass tries again.</summary>
    Offline,

    /// <summary>The file won't open here (passphrase, damage, a newer build). It is left exactly as it is and edits wait.</summary>
    Unreadable,
}

/// <summary>Where the file stands, when it last met Drive, and the sentence a screen can show (empty when nothing needs saying).</summary>
public sealed record PeopleStanding(PeopleFileState State, DateTimeOffset? LastSynced, string Sentence)
{
    public static PeopleStanding NotCompared { get; } = new(PeopleFileState.NotCompared, null, string.Empty);
}

/// <summary>What the app tells the file about the phone it is on, read live. The passphrase is only ever asked whether it is there.</summary>
public sealed record PeopleFileSeams(Func<bool> HasPassphrase, Func<bool> HasAccount, Func<HouseholdFileRole> Role, Func<DateTimeOffset> Now);

/// <summary>
/// The household file (<see cref="PeopleFile.FileName"/>) on one phone, the same way in every app: the phone's copy is the working copy
/// every screen reads and every edit lands in, and Drive is reached only by <see cref="SyncAsync"/>, whose result is merged into the copy
/// as it stands when the pass returns. <b>A file that won't open is never written over</b> (G14): the Drive store refuses before any
/// write, and here every edit is refused while the file stands unreadable. Only the owner's phone may edit (<see cref="CanEdit"/>) and
/// only the owner's account creates the file; an unknown role has the owner's rights here but never creates it.
/// </summary>
public sealed class HouseholdPeopleFile
{
    public const string NoPassphraseSentence = "The household is on this phone only until the vault passphrase is entered.";
    public const string NoAccountSentence = "The household is on this phone only until you sign in to Google.";
    public const string OfflineSentence = "Drive couldn’t be reached; the household will be compared again soon.";
    public const string UnreadableSentence = "The household file in Drive won’t open on this phone, so it’s left exactly as it is. Check the vault passphrase.";
    public const string LocalDamagedSentence = "This phone’s copy of the household won’t open, so it’s left exactly as it is.";

    private readonly IHouseholdStore local;
    private readonly DriveHouseholdStore shared;
    private readonly PeopleInstallStore install;
    private readonly PeopleFileSeams seams;
    private readonly SemaphoreSlim editGate = new(1, 1);
    private readonly SemaphoreSlim passGate = new(1, 1);
    private PeopleStanding standing = PeopleStanding.NotCompared;
    private HouseholdDocument? latest;
    private string? installId;

    public HouseholdPeopleFile(IHouseholdStore local, DriveHouseholdStore shared, PeopleInstallStore install, PeopleFileSeams seams)
    {
        this.local = local ?? throw new ArgumentNullException(nameof(local));
        this.shared = shared ?? throw new ArgumentNullException(nameof(shared));
        this.install = install ?? throw new ArgumentNullException(nameof(install));
        this.seams = seams ?? throw new ArgumentNullException(nameof(seams));
    }

    /// <summary>Raised (off the UI thread) when the copy or the standing changed, so a screen reads again.</summary>
    public event Action? Changed;

    /// <summary>Raised after an edit lands: a pass is wanted.</summary>
    public event Action? SyncWanted;

    public PeopleStanding Standing => Volatile.Read(ref standing);

    public string InstallId => installId ??= install.Load().InstallId;

    /// <summary>Whether this phone may change the household: the owner's (or, before the vault is read, an unknown) role.</summary>
    public bool CanEdit => seams.Role() != HouseholdFileRole.SharedWith;

    /// <summary>The household as this phone's copy last read it; empty before the first read.</summary>
    public HouseholdPeople People => Volatile.Read(ref latest) is { } document ? HouseholdPeople.From(document) : HouseholdPeople.Empty;

    /// <summary>Whether the copy holds anything at all (a phone that has never met the household holds nothing).</summary>
    public bool HasHousehold => Volatile.Read(ref latest) is { } document && document.Records.Count > 0;

    /// <summary>Reads the phone's copy. A damaged copy throws <see cref="HouseholdFormatException"/> and is left as it is.</summary>
    public async Task<HouseholdPeople> ReadAsync(CancellationToken cancellationToken)
    {
        var document = await local.LoadAsync(cancellationToken);
        Volatile.Write(ref latest, document);
        return HouseholdPeople.From(document);
    }

    /// <summary>
    /// One edit of the phone's copy, made by <paramref name="edit"/> with the household's editor, the copy as it stands, a clock that has
    /// witnessed everything the copy holds (each call a newer stamp) and this install's id. A refused edit writes nothing.
    /// </summary>
    public async Task<PeopleEdit> EditAsync(
        Func<HouseholdPeopleEditor, HouseholdDocument, Func<HlcTimestamp>, string, PeopleEdit> edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (Standing.State == PeopleFileState.Unreadable)
        {
            return new PeopleEdit(Volatile.Read(ref latest) ?? HouseholdDocument.Empty, Standing.Sentence);
        }

        PeopleEdit result;
        await editGate.WaitAsync(cancellationToken);
        try
        {
            var document = await local.LoadAsync(cancellationToken);
            var (id, last) = install.Load();
            var clock = new HybridLogicalClock(last);
            clock.Witness(document.MaxStamp, seams.Now().ToUnixTimeMilliseconds());
            result = edit(new HouseholdPeopleEditor(CanEdit), document, () => clock.Now(seams.Now().ToUnixTimeMilliseconds()), id);
            if (!result.Accepted || result.Document.ContentEquals(document))
            {
                Volatile.Write(ref latest, document);
                return result;
            }

            await local.SaveAsync(result.Document, cancellationToken);
            install.SaveClock(clock.Last);
            Volatile.Write(ref latest, result.Document);
        }
        finally
        {
            editGate.Release();
        }

        SyncWanted?.Invoke();
        Changed?.Invoke();
        return result;
    }

    /// <summary>One pass against Drive. Never throws for the file's sake; only the caller leaving (its token) propagates.</summary>
    public async Task<PeopleStanding> SyncAsync(CancellationToken cancellationToken)
    {
        await passGate.WaitAsync(cancellationToken);
        try
        {
            var settled = await PassAsync(cancellationToken);
            Volatile.Write(ref standing, settled);
            Changed?.Invoke();
            return settled;
        }
        finally
        {
            passGate.Release();
        }
    }

    private async Task<PeopleStanding> PassAsync(CancellationToken cancellationToken)
    {
        var last = Standing.LastSynced;
        try
        {
            var mine = await local.LoadAsync(cancellationToken);
            Volatile.Write(ref latest, mine);
            if (!seams.HasPassphrase())
            {
                return new PeopleStanding(PeopleFileState.LocalOnly, last, NoPassphraseSentence);
            }

            if (!seams.HasAccount())
            {
                return new PeopleStanding(PeopleFileState.LocalOnly, last, NoAccountSentence);
            }

            var result = await shared.PublishAsync(mine, cancellationToken);
            await AdoptAsync(result.Held, cancellationToken);
            return result.Outcome switch
            {
                HouseholdPublishOutcome.NotYetShared => new PeopleStanding(PeopleFileState.WaitingForShare, last, WaitingSentence),
                HouseholdPublishOutcome.AlreadyCurrent or HouseholdPublishOutcome.Written
                    when result.Held.Records.Count == 0 && !HouseholdSync.MayCreateFile(seams.Role()) =>
                    new PeopleStanding(PeopleFileState.WaitingForShare, last, WaitingSentence),
                HouseholdPublishOutcome.AlreadyCurrent or HouseholdPublishOutcome.Written => new PeopleStanding(PeopleFileState.Synced, seams.Now(), string.Empty),
                _ => new PeopleStanding(PeopleFileState.Offline, last, OfflineSentence),
            };
        }
        catch (HouseholdUnreadableException)
        {
            return new PeopleStanding(PeopleFileState.Unreadable, last, UnreadableSentence);
        }
        catch (HouseholdFormatException)
        {
            return new PeopleStanding(PeopleFileState.Unreadable, last, LocalDamagedSentence);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Drive unreachable, a refused token, a timeout: the transport's types can't be listed; the next pass tries again.
            return new PeopleStanding(PeopleFileState.Offline, last, OfflineSentence);
        }
    }

    private static string WaitingSentence =>
        "The household appears here once the owner shares '" + PeopleFile.FileName + "' from their Drive.";

    private async Task AdoptAsync(HouseholdDocument published, CancellationToken cancellationToken)
    {
        await editGate.WaitAsync(cancellationToken);
        try
        {
            var now = await local.LoadAsync(cancellationToken);
            var kept = HouseholdSync.Adopt(now, published);
            if (!kept.ContentEquals(now))
            {
                await local.SaveAsync(kept, cancellationToken);
            }

            Volatile.Write(ref latest, kept);
        }
        finally
        {
            editGate.Release();
        }
    }
}
