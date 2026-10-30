using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Constitution.Infrastructure.Vault;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>What the head tells the service about the phone it is on. All read live, none kept: the passphrase is only ever asked whether it is there.</summary>
public sealed record HouseholdFileSeams(
    Func<bool> HasPassphrase, Func<bool> HasAccount, Func<HouseholdFileRole> Role, Func<DateTimeOffset> Now, Func<TimeZoneInfo> Zone);

/// <summary>
/// Constitution's household file on one phone, in the shape Pulp keeps its own: the phone's copy (<c>local</c>) is the working copy
/// every read and edit uses, and the shared file in Drive (<c>shared</c>) is reached only by <see cref="SyncAsync"/>, so a phone
/// without Drive keeps working and loses nothing. A pass is the library's own publish cycle; what it settles on is merged into the
/// phone's copy as it stands when the pass returns, so an edit made while the pass was on the network is kept.
///
/// <para><b>Never over a file that cannot be opened (G14).</b> The Drive store refuses before any write; this service adds that a
/// file which will not open puts the phone in <see cref="HouseholdFileState.Unreadable"/>, where every edit is refused with the
/// reason and nothing is read from the copy as if it were the household. A pass asked for again tries once more.</para>
///
/// <para><b>Only the owner creates the file</b> is the Drive store's <c>mayCreate</c> (the head passes the owner role); a partner or
/// an unknown role waits for the share. Words that reach the trail are fixed sentences; the passphrase is asked for by the store's
/// delegate and is in nothing here.</para>
/// </summary>
public sealed class HouseholdFileService
{
    private readonly IHouseholdStore local;
    private readonly DriveHouseholdStore shared;
    private readonly IHouseholdInstallStore install;
    private readonly HouseholdFileSeams seams;
    private readonly IConstitutionVaultLog log;
    private readonly SemaphoreSlim editGate = new(1, 1);
    private readonly SemaphoreSlim passGate = new(1, 1);
    private HouseholdStanding standing = HouseholdStanding.NotCompared;
    private HouseholdDocument? latest;
    private string? installId;
    private int lastUnreadableCount;

    public HouseholdFileService(
        IHouseholdStore local, DriveHouseholdStore shared, IHouseholdInstallStore install, HouseholdFileSeams seams, IConstitutionVaultLog log)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(seams);
        ArgumentNullException.ThrowIfNull(log);
        this.local = local;
        this.shared = shared;
        this.install = install;
        this.seams = seams;
        this.log = log;
    }

    /// <summary>Raised (off the UI thread) whenever the standing or the phone's copy changed, so a screen reads again.</summary>
    public event Action? Changed;

    /// <summary>Raised after an edit lands: a pass is wanted once the quiet window has passed.</summary>
    public event Action? SyncWanted;

    /// <summary>Raised when a pass ends, with where it left the file; the head's schedule hears success, failure or a block from it.</summary>
    public event Action<HouseholdStanding>? PassSettled;

    public HouseholdStanding Standing => Volatile.Read(ref standing);

    public string FileName => shared.FileName;

    /// <summary>The id this install writes under. Created on first use and kept.</summary>
    public string InstallId => installId ??= install.Load().InstallId;

    public HouseholdFileRole Role => seams.Role();

    /// <summary>The roster-editing role: the partner's is read-only; an unknown role has the owner's rights (and still never creates the file).</summary>
    public RosterRole RosterRole => RosterEditor.RoleFor(Role);

    public DateTimeOffset Now => seams.Now();

    public TimeZoneInfo Zone => seams.Zone();

    /// <summary>The sentence for why nothing may be read from or written to the file now; null when something may.</summary>
    public string? BlockedSentence => Standing.IsUnreadable ? Standing.Sentence : null;

    /// <summary>
    /// The people as the phone's copy last read, for the Settings group to draw without waiting on a file. Null when there is no
    /// household to show: nothing read yet, the file will not open, or the phone's copy is empty and has not met Drive.
    /// </summary>
    public RosterView? Roster
    {
        get
        {
            var document = Volatile.Read(ref latest);
            if (document is null || Standing.IsUnreadable)
            {
                return null;
            }

            var unmet = Standing.State is HouseholdFileState.NotCompared or HouseholdFileState.LocalOnly or HouseholdFileState.WaitingForShare;
            return unmet && document.Records.Count == 0 ? null : RosterView.From(document);
        }
    }

    /// <summary>The phone's copy. A damaged copy throws <see cref="HouseholdFormatException"/> and is left exactly as it is.</summary>
    public async Task<HouseholdDocument> ReadAsync(CancellationToken cancellationToken)
    {
        var document = await local.LoadAsync(cancellationToken);
        Volatile.Write(ref latest, document);
        return document;
    }

    /// <summary>
    /// One edit of the phone's copy as it stands when the write lands, stamped by this install's clock after it has witnessed
    /// everything the copy holds. Refused, with nothing written, while the file is unreadable.
    /// </summary>
    public Task EditAsync(Func<HouseholdDocument, HlcTimestamp, string, HouseholdDocument> edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return EditAsync(
            (document, clock, writer) => edit(document, clock.Witness(document.MaxStamp, seams.Now().ToUnixTimeMilliseconds()), writer),
            cancellationToken);
    }

    /// <summary>The same edit for a writer that needs the clock itself (the lease writes a beat and a claim with one).</summary>
    public async Task EditAsync(Func<HouseholdDocument, HybridLogicalClock, string, HouseholdDocument> edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (Standing is { IsUnreadable: true, Reason: { } reason } blocked)
        {
            throw new HouseholdUnreadableException(reason, blocked.Sentence);
        }

        await editGate.WaitAsync(cancellationToken);
        try
        {
            var document = await local.LoadAsync(cancellationToken);
            var state = install.Load();
            var clock = new HybridLogicalClock(state.LastStamp);
            var changed = edit(document, clock, state.InstallId);
            await local.SaveAsync(changed, cancellationToken);
            install.SaveClock(clock.Last);
            Volatile.Write(ref latest, changed);
        }
        finally
        {
            editGate.Release();
        }

        SyncWanted?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>
    /// One pass against Drive. Never throws for the file's sake: every ending is a <see cref="HouseholdStanding"/> with a fixed
    /// sentence and one trail line. Only the caller leaving (its token) propagates.
    /// </summary>
    public async Task<HouseholdStanding> SyncAsync(CancellationToken cancellationToken)
    {
        await passGate.WaitAsync(cancellationToken);
        try
        {
            var settled = await PassAsync(cancellationToken);
            Set(settled);
            PassSettled?.Invoke(settled);
            if (settled.State == HouseholdFileState.Synced)
            {
                await BeatIfDueAsync(cancellationToken);
            }

            return settled;
        }
        finally
        {
            passGate.Release();
        }
    }

    private async Task<HouseholdStanding> PassAsync(CancellationToken cancellationToken)
    {
        if (!seams.HasPassphrase())
        {
            log.Outcome("sync skipped, no passphrase");
            return new HouseholdStanding(HouseholdFileState.LocalOnly, Standing.LastSynced, HouseholdSentences.NoVaultKey);
        }

        if (!seams.HasAccount())
        {
            log.Outcome("sync skipped, not signed in");
            return new HouseholdStanding(HouseholdFileState.LocalOnly, Standing.LastSynced, HouseholdSentences.NoAccount);
        }

        try
        {
            var mine = await ReadAsync(cancellationToken);
            var result = await shared.PublishAsync(mine, cancellationToken);
            await AdoptAsync(result.Held, cancellationToken);
            var now = seams.Now();
            switch (result.Outcome)
            {
                case HouseholdPublishOutcome.AlreadyCurrent or HouseholdPublishOutcome.Written
                    when result.Held.Records.Count == 0 && !HouseholdSync.MayCreateFile(seams.Role()):
                    // Nothing is held here or in Drive, and this phone may not create the file: the library calls that current,
                    // but there is no household to be in step with until the owner shares one.
                    log.Outcome("sync, shared file not found");
                    return new HouseholdStanding(HouseholdFileState.WaitingForShare, Standing.LastSynced, HouseholdSentences.WaitingForShare(shared.FileName));
                case HouseholdPublishOutcome.AlreadyCurrent or HouseholdPublishOutcome.Written:
                    log.Outcome("sync ok");
                    return new HouseholdStanding(HouseholdFileState.Synced, now, string.Empty);
                case HouseholdPublishOutcome.NotYetShared:
                    log.Outcome("sync, shared file not found");
                    return new HouseholdStanding(HouseholdFileState.WaitingForShare, Standing.LastSynced, HouseholdSentences.WaitingForShare(shared.FileName));
                default:
                    log.Outcome("sync, not written, try again");
                    return new HouseholdStanding(HouseholdFileState.Offline, Standing.LastSynced, HouseholdSentences.OtherPhoneWriting);
            }
        }
        catch (HouseholdUnreadableException exception)
        {
            log.Outcome(HouseholdSentences.UnreadableTrail(exception.Reason));
            return new HouseholdStanding(
                HouseholdFileState.Unreadable, Standing.LastSynced, HouseholdSentences.Unreadable(exception.Reason), exception.Reason, exception);
        }
        catch (HouseholdFormatException exception)
        {
            // This phone's own copy is damaged: nothing it holds can be merged up without guessing, and it is left as it is.
            log.Outcome("sync refused, file here corrupt");
            return new HouseholdStanding(
                HouseholdFileState.Unreadable, Standing.LastSynced, HouseholdSentences.LocalDamaged, HouseholdUnreadableReason.Damaged, exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Drive unreachable, a token refused, a timeout: the Drive client and the HTTP stack throw types this code cannot
            // list, so the failure is kept whole on the standing (stack included) and the phone tries again later.
            log.Outcome("sync offline, try again");
            return new HouseholdStanding(HouseholdFileState.Offline, Standing.LastSynced, HouseholdSentences.Offline, Failure: exception);
        }
    }

    // What the pass settled on, merged into the phone's copy as it stands now.
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

    // The heartbeat is the lease's: written for a known role only (the lease has no unknown role to write), when it is due.
    private async Task BeatIfDueAsync(CancellationToken cancellationToken)
    {
        var role = seams.Role();
        if (role == HouseholdFileRole.Unknown || Volatile.Read(ref latest) is not { } document)
        {
            return;
        }

        var id = InstallId;
        var now = seams.Now();
        if (!LeaseWriter.NeedsHeartbeat(document, id, now))
        {
            return;
        }

        var rosterRole = RosterEditor.RoleFor(role);
        await EditAsync(
            (current, clock, writer) => LeaseWriter.Beat(current, clock, writer, rosterRole, now, LeaseTerms.DefaultLeaseLength),
            cancellationToken);
    }

    /// <summary>Counts the cards a read could not open once per change, as a count only.</summary>
    internal void NoteUnreadable(int unreadable)
    {
        if (unreadable == lastUnreadableCount)
        {
            return;
        }

        lastUnreadableCount = unreadable;
        if (unreadable > 0)
        {
            log.Outcome($"{unreadable} records unreadable");
        }
    }

    private void Set(HouseholdStanding next)
    {
        Volatile.Write(ref standing, next);
        Changed?.Invoke();
    }
}
