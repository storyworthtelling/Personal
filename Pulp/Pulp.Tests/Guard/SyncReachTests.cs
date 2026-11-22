namespace Pulp.Tests.Guard;

/// <summary>
/// What nothing reached was never built: a pass that no screen could ask for, a notice nothing raised,
/// a passphrase a comment said was kept and was not, a launch that never read the vault. Each guard
/// reads the head's source and asserts it found the code it protects.
/// </summary>
public class SyncReachTests
{
    private static string Text(string path) => File.ReadAllText(Path.Combine(PulpSource.Root(), path));

    [Fact]
    public void TheShellAsksForAPassOnResumeAndRaisesTheSyncNotice()
    {
        var shell = Text("Pulp.Android/MainActivity.cs");

        Assert.Contains("SyncResumeGate", shell);
        Assert.Contains("HouseholdSyncNotices", shell);
        Assert.Contains("RequestSync", shell);
    }

    [Fact]
    public void ServicesRefreshTheVaultAtLaunchAndRememberTheRole()
    {
        var services = Text("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("RefreshAtLaunchAsync", services);
        Assert.Contains("SaveRole", services);
        Assert.Contains("HouseholdRoleMemory.Effective", services);
    }

    [Fact]
    public void TheSyncNowButtonExistsAndCallsSyncNow()
    {
        var button = Text("Pulp.Android/Settings/SyncNowButton.cs");

        Assert.Contains("SyncLine", button);
        Assert.Contains("SyncNowAsync", button);
    }

    [Fact]
    public void FirstRunKeepsThePassphraseAsItIsTyped()
    {
        var firstRun = Text("Pulp.Android/FirstRun/FirstRunActivity.cs");

        Assert.Contains("passphrase.TextChanged", firstRun);
        Assert.Contains("VaultPassphraseStore.Passphrase = ", firstRun);
        Assert.Contains("kept as it is typed", firstRun);
    }

    [Fact]
    public void AddPeopleWritesTheChildrensRulesThroughTheSameEdit()
    {
        var services = Text("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("HouseholdDraftWrite.Apply", services);
    }

    // Orphan audit §29: a league whose feed was down showed old games with no word.
    [Fact]
    public void TheSportsTabRaisesTheUnreachableFeedNotice()
    {
        Assert.Contains("SportsUnreachable", Text("Pulp.Android/App/PulpServices.cs"));
        var screen = Text("Pulp.Android/Sports/SportsScreen.cs");
        Assert.Contains("services.SportsUnreachable", screen);
        Assert.Contains("WaitingNotices.SportsFeedUnreachable(", screen);
    }

    // Orphan audit §28: the mis-landing check (D122) was computed nowhere, so its dog-ear never showed.
    [Fact]
    public void AStartedHandOffIsWatchedAndRelayedSessionsSettleIt()
    {
        Assert.Contains("Landings.Watch(", Text("Pulp.Android/Components/PulpNotices.cs"));
        Assert.Contains("Landings.Saw(", Text("Pulp.Android/Sessions/SessionReceiver.cs"));
        var landings = Text("Pulp.Android/HandOffs/Landings.cs");
        Assert.Contains("LandingWatch", landings);
        Assert.Contains("PulpNotices.AfterLanding(", landings);
        Assert.Contains("LandingCheck.Window", landings);
    }

    // A notice with a button the page could not serve threw from ToDogEar, inside a posted Settle, and
    // crashed the app; the exception's words carried the notice's headline, a watched title.
    [Fact]
    public void ANoticeButtonWithNoHandlerIsDroppedNotThrown()
    {
        var notices = Text("Pulp.Android/Components/PulpNotices.cs");
        var toDogEar = notices[notices.IndexOf("private static DogEar.Notice ToDogEar", StringComparison.Ordinal)..];

        Assert.DoesNotContain("throw new ArgumentException", toDogEar);
        Assert.DoesNotContain("notice.Headline}", toDogEar);
        Assert.Contains("canReopen:", notices);
    }

    [Fact]
    public void EachSettledLandingIsCaughtOnItsOwnAndLoggedByShape()
    {
        var landings = Text("Pulp.Android/HandOffs/Landings.cs");
        var settle = landings[landings.IndexOf("private static void Settle", StringComparison.Ordinal)..];

        Assert.Contains("catch (Exception", settle);
        Assert.Contains("HeadLog.Quiet(", settle);
        Assert.DoesNotContain("TitleName}", settle);
    }

    // W7.5 (owner's ruling): the weekly refresh had frozen names and no worker; it now runs, daily, under them.
    [Fact]
    public void TheWeeklyRefreshIsScheduledUnderItsFrozenNames()
    {
        var worker = Text("Pulp.Android/Catalogue/WeeklyRefreshWorker.cs");
        Assert.Contains("[global::Android.Runtime.Register(PulpIdentity.WeeklyRefreshWorkerName)]", worker);
        Assert.Contains("EnqueueUniquePeriodicWork(PulpIdentity.WeeklyRefreshWorkName, ExistingPeriodicWorkPolicy.Keep!", worker);
        Assert.Contains("RefreshTrackedAsync(", worker);

        var services = Text("Pulp.Android/App/PulpServices.cs");
        Assert.Contains("WeeklyRefreshSchedule.Schedule(context);", services);
        Assert.Contains("WeeklyRefresh.Due(", services);
    }
}
