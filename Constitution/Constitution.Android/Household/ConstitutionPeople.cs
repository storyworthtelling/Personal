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
/// <item>Read the household from Drive. <b>Constitution only reads it</b> (<see cref="PeopleFileAccess.Reader"/>): Foreword owns the
/// people and the rhythm, and nothing here writes either back.</item>
/// <item>Make this phone's planning setup the household's, keeping who this phone delivers for.</item>
/// <item>Mirror the household into Constitution's roster (<see cref="HouseholdMirror"/>, the owner's phone only, since the roster has one
/// writer).</item>
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
            new PeopleFileSeams(() => VaultPassphraseStore.IsSet, () => VoiceGoogleAccount.IsSignedIn, role, () => DateTimeOffset.UtcNow),
            PeopleFileAccess.Reader);
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
            var household = File.People;

            // The week's setup follows first and on its own, so a roster that can't be mirrored never keeps the calendars from arriving.
            Step("setup", () => setup.FollowHousehold(household));
            if (household.People.Count > 0 && service.RosterRole == RosterRole.Owner && service.BlockedSentence is null)
            {
                await StepAsync("roster", () => service.EditAsync(
                    (document, clock, writer) => HouseholdMirror.Apply(
                        document, household, () => clock.Now(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), writer),
                    cancellationToken));
            }

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

    private static void Step(string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HeadLog.Warn(LogTag, $"The household {name} didn’t follow: " + exception.GetType().Name);
        }
    }

    private static async Task StepAsync(string name, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HeadLog.Warn(LogTag, $"The household {name} didn’t follow: " + exception.GetType().Name);
        }
    }
}
