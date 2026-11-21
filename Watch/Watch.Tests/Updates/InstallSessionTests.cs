using System.Text.RegularExpressions;
using Storyworthtelling.Updates;
using Watch.Domain;
using Watch.Domain.Updates;
using Watch.Tests.Guard;

namespace Watch.Tests.Updates;

/// <summary>
/// P14 release A fix round, finding 5. The installer abandons the session still waiting for an app before it begins one
/// for a newer (or the same) build, so the wrist has one prompt per app; the platform then reports the abandoned session
/// as aborted. Taken as the app's outcome, that recorded a decline nobody made over the new build's waiting result, cleared
/// the new build's card, and stopped the phone sending the build again. A waiting session the platform lets go after its
/// limit came back aborted too, a permanent decline. Each status is now judged by the session it is for. Session ids and
/// versions are invented.
/// </summary>
public class InstallSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private static InstallSession Dash(int session, long versionCode = 1100000005, DateTimeOffset? began = null) =>
        new(WatchIdentity.DashPackage, versionCode, session, began ?? Now - TimeSpan.FromMinutes(5));

    // The record after a resend: session 7 waited for the build, the installer abandoned it and began session 8.
    private static InstallSessionRecord Resent()
    {
        var record = InstallSessions.Began(InstallSessionRecord.Empty, Dash(7, began: Now - TimeSpan.FromDays(1)));
        record = InstallSessions.Abandoning(record, 7);
        return InstallSessions.Began(record, Dash(8));
    }

    [Fact]
    public void AStatusFromASessionTheInstallerAbandonedIsIgnored()
    {
        var record = Resent();

        Assert.Equal(SessionVerdict.Ignore, InstallSessions.Judge(record, WatchIdentity.DashPackage, 7, InstallerStatus.Aborted, Now));
        Assert.Equal(SessionVerdict.Ignore, InstallSessions.Judge(record, WatchIdentity.DashPackage, 7, InstallerStatus.Failure, Now));
    }

    [Fact]
    public void AStatusFromAnotherOfTheAppsSessionsIsIgnored()
    {
        // Not the app's current session, and not one this record abandoned (a record from before the fix, say).
        var record = InstallSessions.Began(InstallSessionRecord.Empty, Dash(8));

        Assert.Equal(SessionVerdict.Ignore, InstallSessions.Judge(record, WatchIdentity.DashPackage, 5, InstallerStatus.Aborted, Now));
    }

    [Theory]
    [InlineData(InstallerStatus.PendingUserAction)]
    [InlineData(InstallerStatus.Success)]
    [InlineData(InstallerStatus.Conflict)]
    [InlineData(InstallerStatus.Aborted)]
    public void TheCurrentSessionSettles(InstallerStatus status)
    {
        Assert.Equal(SessionVerdict.Settle, InstallSessions.Judge(Resent(), WatchIdentity.DashPackage, 8, status, Now));
    }

    [Fact]
    public void AnAbortSoonAfterTheCardIsADecline()
    {
        var record = InstallSessions.Began(InstallSessionRecord.Empty, Dash(8, began: Now - TimeSpan.FromHours(2)));

        Assert.Equal(SessionVerdict.Settle, InstallSessions.Judge(record, WatchIdentity.DashPackage, 8, InstallerStatus.Aborted, Now));
        Assert.Equal(WatchInstallOutcome.Declined, WatchInstallOutcomes.FromStatus(InstallerStatus.Aborted, askedBefore: true));
    }

    [Theory]
    [InlineData(72 * 60)]
    [InlineData((72 * 60) - 30)]
    [InlineData(10 * 24 * 60)]
    public void AnAbortPastThePlatformsLimitIsLetGo(int minutesWaiting)
    {
        var record = InstallSessions.Began(InstallSessionRecord.Empty, Dash(8, began: Now - TimeSpan.FromMinutes(minutesWaiting)));

        Assert.Equal(SessionVerdict.LetGo, InstallSessions.Judge(record, WatchIdentity.DashPackage, 8, InstallerStatus.Aborted, Now));
    }

    [Theory]
    [InlineData(48 * 60)]
    [InlineData((72 * 60) - 61)]
    public void AnAbortBeforeThePlatformsLimitIsTheWrists(int minutesWaiting)
    {
        var record = InstallSessions.Began(InstallSessionRecord.Empty, Dash(8, began: Now - TimeSpan.FromMinutes(minutesWaiting)));

        Assert.Equal(SessionVerdict.Settle, InstallSessions.Judge(record, WatchIdentity.DashPackage, 8, InstallerStatus.Aborted, Now));
    }

    [Theory]
    [InlineData(InstallerStatus.Failure)]
    [InlineData(InstallerStatus.Timeout)]
    [InlineData(InstallerStatus.Success)]
    public void OnlyAnAbortIsLetGo(InstallerStatus status)
    {
        var record = InstallSessions.Began(InstallSessionRecord.Empty, Dash(8, began: Now - TimeSpan.FromDays(4)));

        Assert.Equal(SessionVerdict.Settle, InstallSessions.Judge(record, WatchIdentity.DashPackage, 8, status, Now));
    }

    [Fact]
    public void TheLimitIsThePlatforms()
    {
        Assert.Equal(TimeSpan.FromDays(3), InstallSessions.PlatformLetsGoAfter);
        Assert.Equal(TimeSpan.FromHours(1), InstallSessions.Slack);
        Assert.Equal(WatchSendPlan.WaitingOffer, InstallSessions.PlatformLetsGoAfter);
    }

    [Fact]
    public void ALetGoBuildIsOfferedAgain()
    {
        // Let go, the build's record stays waiting (nothing is recorded over it), and the phone's own rule for a card
        // left unanswered offers it again: the decline that never happened does not stop it.
        var results = InstallResults.Record([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.Waiting));
        var record = InstallSessions.Began(InstallSessionRecord.Empty, Dash(8, began: Now - TimeSpan.FromDays(3)));
        Assert.Equal(SessionVerdict.LetGo, InstallSessions.Judge(record, WatchIdentity.DashPackage, 8, InstallerStatus.Aborted, Now));

        var decisions = WatchSendPlan.Decide(
            [new ReleaseApk(1, new InstallCandidate(WatchIdentity.DashPackage, 1100000005, ApkForm.Watch))],
            WatchAnswer.Answered(new InstalledReport([], results)),
            [new SentApk(WatchIdentity.DashPackage, 1100000005, Now - TimeSpan.FromDays(3))], Now);

        Assert.Equal(WatchSendVerdict.Send, Assert.Single(decisions).Verdict);
    }

    [Fact]
    public void WithNoSessionTheStatusSettlesAsItAlwaysDid()
    {
        Assert.Equal(SessionVerdict.Settle, InstallSessions.Judge(Resent(), WatchIdentity.DashPackage, null, InstallerStatus.Aborted, Now));
        Assert.Equal(SessionVerdict.Settle, InstallSessions.Judge(InstallSessionRecord.Empty, WatchIdentity.DashPackage, 8, InstallerStatus.Success, Now));
    }

    [Fact]
    public void OtherAppsSessionsDoNotCount()
    {
        var record = InstallSessions.Began(Resent(), new InstallSession(WatchIdentity.WavePackage, 1100000009, 9, Now));

        Assert.Equal(SessionVerdict.Settle, InstallSessions.Judge(record, WatchIdentity.WavePackage, 9, InstallerStatus.Aborted, Now));
        Assert.Equal(SessionVerdict.Settle, InstallSessions.Judge(record, WatchIdentity.DashPackage, 8, InstallerStatus.Success, Now));
    }

    [Fact]
    public void BeginningReplacesTheAppsSession()
    {
        var record = InstallSessions.Began(InstallSessions.Began(InstallSessionRecord.Empty, Dash(7)), Dash(8, versionCode: 1100000006));

        var current = Assert.Single(record.Current);
        Assert.Equal(8, current.SessionId);
        Assert.Equal(1100000006, current.VersionCode);
    }

    [Fact]
    public void AbandoningKeepsTheNewestIds()
    {
        var record = InstallSessionRecord.Empty;
        for (var id = 1; id <= InstallSessions.AbandonedKept + 10; id++)
        {
            record = InstallSessions.Abandoning(record, id);
        }

        Assert.Equal(InstallSessions.AbandonedKept, record.Abandoned.Count);
        Assert.Contains(InstallSessions.AbandonedKept + 10, record.Abandoned);
        Assert.DoesNotContain(1, record.Abandoned);
        Assert.InRange(InstallSessions.AbandonedKept, 8, 64);
    }

    [Fact]
    public void AbandoningTheCurrentSessionLeavesNoCurrentSession()
    {
        var record = InstallSessions.Abandoning(InstallSessions.Began(InstallSessionRecord.Empty, Dash(7)), 7);

        Assert.Empty(record.Current);
        Assert.Equal(SessionVerdict.Ignore, InstallSessions.Judge(record, WatchIdentity.DashPackage, 7, InstallerStatus.Aborted, Now));
    }

    [Fact]
    public void EndedForgetsOnlyThatSession()
    {
        var record = Resent();

        Assert.Single(InstallSessions.Ended(record, WatchIdentity.DashPackage, 7).Current);
        Assert.Empty(InstallSessions.Ended(record, WatchIdentity.DashPackage, 8).Current);
        Assert.Single(InstallSessions.Ended(record, WatchIdentity.WavePackage, 8).Current);
    }

    [Fact]
    public void TheRecordRoundTrips()
    {
        var record = InstallSessions.Began(Resent(), new InstallSession(WatchIdentity.FacePackage, 1100000002, 12, Now));

        var read = InstallSessions.Decode(InstallSessions.Encode(record));

        Assert.Equal(record.Current, read.Current);
        Assert.Equal(record.Abandoned, read.Abandoned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"v\":1}")]
    [InlineData("{\"v\":1,\"current\":[{\"package\":7}],\"abandoned\":[]}")]
    [InlineData("{\"v\":1,\"current\":[],\"abandoned\":[\"seven\"]}")]
    public void ADamagedRecordReadsEmpty(string? stored)
    {
        var read = InstallSessions.Decode(stored);

        Assert.Empty(read.Current);
        Assert.Empty(read.Abandoned);
    }

    [Fact]
    public void TheRecordSaysPackagesNumbersAndTimesOnly()
    {
        var stored = InstallSessions.Encode(Resent());

        Assert.Contains(WatchIdentity.DashPackage, stored, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[A-Za-z]:\\|/data/|\.apk", stored);
    }

    [Fact]
    public void TheLinesSayWhatHappenedInWordsAndNumbersOnly()
    {
        Assert.Equal("UPDATE com.storyworthtelling.focusphone.communication 1100000005: a status for a session this installer abandoned, ignored",
            UpdateLines.Ignored(WatchIdentity.DashPackage, 1100000005));
        Assert.Equal("UPDATE com.storyworthtelling.focusphone.communication 1100000005: the platform let the waiting prompt go, so it’s offered again",
            UpdateLines.LetGo(WatchIdentity.DashPackage, 1100000005));
    }

    // ---- the action's word in the status intent ----

    [Fact]
    public void TheActionWordsAreFrozen()
    {
        Assert.Equal("refuse", WatchUpdateActions.Word(WatchUpdateAction.Refuse));
        Assert.Equal("first-install", WatchUpdateActions.Word(WatchUpdateAction.FirstInstall));
        Assert.Equal("asked-update", WatchUpdateActions.Word(WatchUpdateAction.AskedUpdate));
        Assert.Equal("silent-update", WatchUpdateActions.Word(WatchUpdateAction.SilentUpdate));

        var source = File.ReadAllText(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Watch.Domain", "Updates", "InstallSessions.cs"));
        Assert.DoesNotContain("nameof(", Regex.Replace(source, @"///.*|//.*", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void ActionWordsRoundTrip()
    {
        foreach (var action in Enum.GetValues<WatchUpdateAction>())
        {
            Assert.Equal(action, WatchUpdateActions.FromWord(WatchUpdateActions.Word(action)));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FirstInstall")]
    [InlineData("First-Install")]
    [InlineData("first-install ")]
    public void AnyOtherActionWordIsNone(string? word)
    {
        Assert.Null(WatchUpdateActions.FromWord(word));
    }
}
