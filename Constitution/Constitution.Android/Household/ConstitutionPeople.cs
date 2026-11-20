using Android.Content;
using Codex.Ui;
using Codex.Ui.Voice;
using Constitution.Domain.Calendar;
using Constitution.Domain.Household;
using Constitution.Infrastructure.Vault;
using Constitution.Services;
using Constitution.Settings;
using Storyworthtelling.Household;
using Storyworthtelling.People;
using Storyworthtelling.Voice;

namespace Constitution.Household;

/// <summary>
/// The household Foreword owns (<see cref="PeopleFile.FileName"/>), read on this phone. One round
/// (<see cref="RefreshAsync"/>):
/// <list type="number">
/// <item>Meet the household in Drive.</item>
/// <item>On the owner's phone, bring Constitution's own people and its rhythm into the household once. They are linked by name or kept
/// under their own ids, so Foreword can take over without anyone being re-entered.</item>
/// <item>Mirror the household into Constitution's roster (<see cref="HouseholdMirror"/>, the owner's phone only, since the roster has one
/// writer).</item>
/// <item>Make this phone's planning setup the household's, keeping who this phone delivers for.</item>
/// </list>
/// It runs after every household pass and at the start of each planning worker. It never throws; a failure is logged by type.
/// </summary>
public sealed class ConstitutionPeople
{
    private const string LogTag = "People";
    private static readonly object InstanceGate = new();
    private static ConstitutionPeople? instance;

    private readonly Context context;
    private readonly SemaphoreSlim roundGate = new(1, 1);

    private ConstitutionPeople(Context applicationContext)
    {
        context = applicationContext;
        var folder = Path.Combine(
            context.NoBackupFilesDir?.AbsolutePath ?? throw new InvalidOperationException("The app has no private files folder to keep the household in."),
            Folder);
        var account = ConstitutionGoogle.Create();
        Func<HouseholdFileRole> role = () => ConstitutionHousehold.For(context).Role;
        File = new HouseholdPeopleFile(
            new LocalHouseholdStore(folder, "people.json"),
            new DriveHouseholdStore(
                new DriveTextFile(ConstitutionVaults.DriveFactory(account), PeopleFile.FileName, SharedDriveFileIds.Store, readCache: DriveReadCache.Shared),
                PeopleFile.FileName,
                () => VaultPassphraseStore.Passphrase,
                () => HouseholdSync.MayCreateFile(role())),
            new PeopleInstallStore(folder),
            new PeopleFileSeams(() => VaultPassphraseStore.IsSet, () => VoiceGoogleAccount.IsSignedIn, role, () => DateTimeOffset.UtcNow));
    }

    /// <summary>This phone's copy of the household lives here, beside (never inside) Constitution's own household file.</summary>
    public const string Folder = "household-people";

    public HouseholdPeopleFile File { get; }

    /// <summary>True once Foreword's household has people: Constitution's Household group then shows it rather than edits it.</summary>
    public bool ForewordOwns => File.People.People.Count > 0;

    public static ConstitutionPeople For(Context context)
    {
        lock (InstanceGate)
        {
            return instance ??= new ConstitutionPeople(context.ApplicationContext ?? context);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await roundGate.WaitAsync(cancellationToken);
        try
        {
            var service = ConstitutionHousehold.For(context).Service;
            var setup = new HouseholdSetupStore(context, () => service.Roster);
            await File.ReadAsync(cancellationToken);
            var standing = await File.SyncAsync(cancellationToken);

            if (File.CanEdit && standing.State == PeopleFileState.Synced && await JoinTheHouseholdAsync(service, setup, cancellationToken))
            {
                standing = await File.SyncAsync(cancellationToken);
            }

            var household = File.People;
            if (household.People.Count > 0 && service.RosterRole == RosterRole.Owner && service.BlockedSentence is null)
            {
                await service.EditAsync(
                    (document, clock, writer) => HouseholdMirror.Apply(
                        document, household, () => clock.Now(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), writer),
                    cancellationToken);
            }

            setup.FollowHousehold(household);
            HeadLog.Info(LogTag, $"Household round: {standing.State}, {household.People.Count} people");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A damaged copy, a file that won't open, Drive away: the round is skipped and logged by type, never by value.
            HeadLog.Warn(LogTag, "A household round didn’t complete: " + exception.GetType().Name);
        }
        finally
        {
            roundGate.Release();
        }
    }

    // The owner's phone, once: Constitution's own people (with their ids, birthdays and pickup) and the rhythm its setup knew.
    private async Task<bool> JoinTheHouseholdAsync(
        Constitution.Infrastructure.Household.HouseholdFileService service, HouseholdSetupStore setup, CancellationToken cancellationToken)
    {
        var mine = HouseholdMirror.ForImport(await service.ReadAsync(cancellationToken));
        var known = HouseholdMirror.Rhythm(setup.Read() is HouseholdSetupRead.Configured configured ? configured.Setup : null);
        var wrote = false;
        await File.EditAsync((editor, document, next, writer) =>
        {
            var changed = editor.Import(document, PeopleApps.Constitution, mine, next, writer).Document;
            changed = editor.FillRhythm(changed, known, next(), writer).Document;
            wrote = !changed.ContentEquals(document);
            return new PeopleEdit(changed, null);
        }, cancellationToken);
        return wrote;
    }
}
