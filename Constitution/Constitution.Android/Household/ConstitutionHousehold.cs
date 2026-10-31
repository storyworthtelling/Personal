using Android.Content;
using Codex.Ui.Voice;
using Constitution.Domain;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.Vault;
using Constitution.Services;
using Constitution.Settings;
using Storyworthtelling.Household;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Household;

/// <summary>
/// The household file on this phone, assembled: the phone's copy under the no-backup folder, the shared file in the owner's Drive
/// (<see cref="ConstitutionIdentity.HouseholdFileName"/>, over the same Drive seam and account the vault uses), the install's own id
/// and clock, and the library's sync loop. It is the shape Pulp keeps its household in, with the screens' reading and writing in
/// <see cref="HouseholdFileService"/> (pure, tested with a fake Drive) and this class only the wiring.
///
/// <para><b>The passphrase is read, never kept.</b> The Drive store asks <see cref="VaultPassphraseStore"/> each time it opens the
/// file; nothing here copies, stores, logs or passes it on but to the vault reader that opens the owner marker. <b>Only the owner
/// creates the file</b>: the create rule is the library's <see cref="HouseholdSync.MayCreateFile"/> on the role the vault says, or
/// the one it last said for this account, and an unknown role waits.</para>
///
/// <para>A pass runs when the app opens and when it comes back (at most once a minute, <see cref="SyncResumeGate"/>); an edit asks
/// for one after the library's quiet window, a failure backs off, and a file that will not open stops passes until asked again.</para>
/// </summary>
public sealed class ConstitutionHousehold
{
    private static readonly object InstanceGate = new();
    private static ConstitutionHousehold? instance;

    private readonly VoiceGoogleAccount account;
    private readonly FileHouseholdInstallStore install;
    private readonly HouseholdSyncLoop loop;
    private readonly IConstitutionVaultLog log;
    private readonly Context context;
    private RememberedRole? remembered;
    private int liveRole = (int)HouseholdFileRole.Unknown;
    private bool opened;
    private DateTimeOffset? lastResumeAsk;

    private ConstitutionHousehold(Context applicationContext)
    {
        context = applicationContext;
        var folder = Path.Combine(
            context.NoBackupFilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no private files folder to keep the household file in."),
            HouseholdFolder);
        log = new ConstitutionVaultLog(context);
        account = ConstitutionGoogle.Create();
        install = new FileHouseholdInstallStore(folder);
        remembered = install.Load().Role;

        var shared = new DriveHouseholdStore(
            new DriveTextFile(ConstitutionVaults.DriveFactory(account), ConstitutionIdentity.HouseholdFileName, SharedDriveFileIds.Store),
            ConstitutionIdentity.HouseholdFileName,
            () => VaultPassphraseStore.Passphrase,
            () => HouseholdSync.MayCreateFile(Role));
        Service = new HouseholdFileService(
            new LocalHouseholdStore(folder, LocalFileName),
            shared,
            install,
            new HouseholdFileSeams(() => VaultPassphraseStore.IsSet, () => VoiceGoogleAccount.IsSignedIn, () => Role, () => DateTimeOffset.UtcNow, () => TimeZoneInfo.Local),
            log);
        Planning = new HouseholdPlanningStanding(Service);

        // The loop outlives any one pass: a failure is logged by type and backed off, never fatal.
        loop = new HouseholdSyncLoop(
            async token => await Service.SyncAsync(token),
            exception => Android.Util.Log.Warn(LogTag, "A household pass failed unexpectedly: " + exception.GetType().Name),
            () => DateTimeOffset.UtcNow);
        Service.SyncWanted += loop.Edited;
        Service.PassSettled += OnPassSettled;
    }

    /// <summary>The folder under the no-backup directory; the household never goes into a backup.</summary>
    public const string HouseholdFolder = "household";

    /// <summary>The phone's own copy of the household file. The shared file's name is frozen elsewhere; this one is the phone's alone.</summary>
    public const string LocalFileName = "household.json";

    private const string LogTag = "Household";

    public HouseholdFileService Service { get; }

    /// <summary>The weekly meal run asks this before it plans (<c>MealWeekServices</c>, through the shared lease); the workout run does not ask it.</summary>
    public HouseholdPlanningStanding Planning { get; }

    /// <summary>
    /// Whose phone this is: what the vault says now, else what it said last for this account. Unknown only when neither exists,
    /// which has the owner's rights over the roster and never creates the shared file.
    /// </summary>
    public HouseholdFileRole Role =>
        HouseholdRoleMemory.Effective((HouseholdFileRole)Volatile.Read(ref liveRole), Volatile.Read(ref remembered), VoiceGoogleAccount.Email);

    public static ConstitutionHousehold For(Context context)
    {
        lock (InstanceGate)
        {
            return instance ??= new ConstitutionHousehold(context.ApplicationContext ?? context);
        }
    }

    /// <summary>The app opened: starts the loop (a pass is due at once) and reads the vault's owner marker off the UI thread.</summary>
    public void Opened()
    {
        if (opened)
        {
            return;
        }

        opened = true;
        loop.Opened();
        _ = Task.Run(() => loop.RunAsync(CancellationToken.None));
        _ = Task.Run(() => RefreshRoleAsync(CancellationToken.None));
    }

    /// <summary>The app came back: a pass is asked for, at most once a minute.</summary>
    public void Resumed()
    {
        var now = DateTimeOffset.UtcNow;
        if (!SyncResumeGate.ShouldAsk(lastResumeAsk, now))
        {
            return;
        }

        lastResumeAsk = now;
        loop.Ask();
    }

    /// <summary>A passphrase was just entered or the account just signed in: read the owner marker and compare again.</summary>
    public void AskNow()
    {
        _ = Task.Run(() => RefreshRoleAsync(CancellationToken.None));
        loop.Ask();
    }

    private async Task RefreshRoleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var store = new DriveSecretsVaultStore(new DriveTextFile(
                ConstitutionVaults.DriveFactory(account), DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store));
            var read = await HouseholdRoleReader.ReadAsync(store, VaultPassphraseStore.Passphrase, VoiceGoogleAccount.Email, cancellationToken);
            if (read.Role == HouseholdFileRole.Unknown)
            {
                return;
            }

            SharedDriveFileIds.SeedFrom(read.DriveFileIds);
            Volatile.Write(ref liveRole, (int)read.Role);
            Remember(read.Role);
            loop.Ask();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The vault read goes through the Google client and the HTTP stack, whose failures cannot be listed; the phone carries
            // on with the role it remembers, and the failure is on the trail by type, never by message.
            log.Outcome("vault unreachable");
            Android.Util.Log.Warn(LogTag, "The vault's owner marker could not be read: " + exception.GetType().Name);
        }
    }

    // The role the vault just said is kept under the account it was read for, so the next cold start has it before the vault is read.
    private void Remember(HouseholdFileRole role)
    {
        var email = VoiceGoogleAccount.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var next = new RememberedRole(role, email);
        if (Volatile.Read(ref remembered) == next)
        {
            return;
        }

        install.SaveRole(role, email);
        Volatile.Write(ref remembered, next);
    }

    private void OnPassSettled(HouseholdStanding standing)
    {
        var now = DateTimeOffset.UtcNow;

        // D109: a file that will not open also waits behind the dog-ear; it goes when the file opens again.
        ConnectionNoticeBoard.With(board => HouseholdNotices.Apply(board, standing, now));
        loop.Update(schedule =>
        {
            switch (standing.State)
            {
                case HouseholdFileState.Synced:
                    schedule.Succeeded();
                    break;
                case HouseholdFileState.Offline or HouseholdFileState.WaitingForShare:
                    schedule.Failed(now);
                    break;
                case HouseholdFileState.Unreadable:
                    schedule.Blocked();
                    break;
            }
        });
    }
}
