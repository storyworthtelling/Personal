using System.Text.RegularExpressions;
using Watch.Domain;
using Watch.Domain.Kit;
using Watch.Domain.Updates;

namespace Watch.Tests.Updates;

/// <summary>
/// What an install came to on the wrist, what the phone does next, and what is logged: the outcome and the platform's
/// status word, never anything from the APK or a message (P14 W-3.1.2, plan §5).
/// </summary>
public class InstallOutcomeTests
{
    [Theory]
    [InlineData(InstallerStatus.PendingUserAction, WatchInstallOutcome.Waiting)]
    [InlineData(InstallerStatus.Success, WatchInstallOutcome.Installed)]
    [InlineData(InstallerStatus.Aborted, WatchInstallOutcome.Declined)]
    [InlineData(InstallerStatus.Failure, WatchInstallOutcome.Failed)]
    [InlineData(InstallerStatus.Blocked, WatchInstallOutcome.Failed)]
    [InlineData(InstallerStatus.Invalid, WatchInstallOutcome.Failed)]
    [InlineData(InstallerStatus.Conflict, WatchInstallOutcome.Failed)]
    [InlineData(InstallerStatus.Storage, WatchInstallOutcome.Failed)]
    [InlineData(InstallerStatus.Incompatible, WatchInstallOutcome.Failed)]
    [InlineData(InstallerStatus.Timeout, WatchInstallOutcome.Failed)]
    [InlineData(InstallerStatus.Unrecognised, WatchInstallOutcome.Failed)]
    public void StatusMeansOneOutcome(InstallerStatus status, WatchInstallOutcome outcome)
    {
        Assert.Equal(outcome, WatchInstallOutcomes.FromStatus(status, askedBefore: false));
    }

    [Fact]
    public void EveryStatusIsClassified()
    {
        foreach (var status in Enum.GetValues<InstallerStatus>())
        {
            Assert.True(Enum.IsDefined(WatchInstallOutcomes.FromStatus(status, askedBefore: false)), status.ToString());
        }
    }

    [Fact]
    public void SuccessAfterAskingIsSaid()
    {
        // Risk R2: whether silent updates work on Wear OS is learnt from which of these two the first update comes to.
        Assert.Equal(WatchInstallOutcome.InstalledAfterAsking, WatchInstallOutcomes.FromStatus(InstallerStatus.Success, askedBefore: true));
    }

    [Fact]
    public void PendingIsWaitingEitherWay()
    {
        Assert.Equal(WatchInstallOutcome.Waiting, WatchInstallOutcomes.FromStatus(InstallerStatus.PendingUserAction, askedBefore: true));
    }

    [Fact]
    public void AFailureAfterAskingIsAFailure()
    {
        Assert.Equal(WatchInstallOutcome.Failed, WatchInstallOutcomes.FromStatus(InstallerStatus.Conflict, askedBefore: true));
    }

    [Theory]
    [InlineData(WatchInstallOutcome.Installed, false)]
    [InlineData(WatchInstallOutcome.InstalledAfterAsking, false)]
    [InlineData(WatchInstallOutcome.Waiting, false)]
    [InlineData(WatchInstallOutcome.Declined, false)]
    [InlineData(WatchInstallOutcome.AlreadyCurrent, false)]
    [InlineData(WatchInstallOutcome.NotForThisWatch, false)]
    [InlineData(WatchInstallOutcome.NotOurs, false)]
    [InlineData(WatchInstallOutcome.Damaged, true)]
    [InlineData(WatchInstallOutcome.Failed, true)]
    public void OnlyFailuresAreSentAgain(WatchInstallOutcome outcome, bool again)
    {
        // A prompt waiting on the wrist, or one the person turned down, is not answered by sending the same APK again.
        Assert.Equal(again, WatchInstallOutcomes.SendAgain(outcome));
    }

    [Fact]
    public void TheOutcomeWordsAreFrozen()
    {
        // Wire words: the phone reads them off update/installed.
        Assert.Equal("installed", WatchInstallOutcomes.Word(WatchInstallOutcome.Installed));
        Assert.Equal("installed-after-asking", WatchInstallOutcomes.Word(WatchInstallOutcome.InstalledAfterAsking));
        Assert.Equal("waiting", WatchInstallOutcomes.Word(WatchInstallOutcome.Waiting));
        Assert.Equal("declined", WatchInstallOutcomes.Word(WatchInstallOutcome.Declined));
        Assert.Equal("current", WatchInstallOutcomes.Word(WatchInstallOutcome.AlreadyCurrent));
        Assert.Equal("not-for-this-watch", WatchInstallOutcomes.Word(WatchInstallOutcome.NotForThisWatch));
        Assert.Equal("not-ours", WatchInstallOutcomes.Word(WatchInstallOutcome.NotOurs));
        Assert.Equal("damaged", WatchInstallOutcomes.Word(WatchInstallOutcome.Damaged));
        Assert.Equal("failed", WatchInstallOutcomes.Word(WatchInstallOutcome.Failed));
    }

    [Fact]
    public void TheStatusWordsAreFrozen()
    {
        string[] expected = ["pending-user-action", "success", "failure", "blocked", "aborted", "invalid", "conflict", "storage", "incompatible", "timeout", "unrecognised"];

        Assert.Equal(expected, Enum.GetValues<InstallerStatus>().Select(WatchInstallOutcomes.Word));
    }

    [Fact]
    public void WordsRoundTrip()
    {
        foreach (var outcome in Enum.GetValues<WatchInstallOutcome>())
        {
            Assert.Equal(outcome, WatchInstallOutcomes.FromWord(WatchInstallOutcomes.Word(outcome)));
        }
        foreach (var status in Enum.GetValues<InstallerStatus>())
        {
            Assert.Equal(status, WatchInstallOutcomes.StatusFromWord(WatchInstallOutcomes.Word(status)));
        }
    }

    [Theory]
    [InlineData("Installed")]
    [InlineData("installed ")]
    [InlineData("done")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyOtherWordIsNoOutcome(string? word)
    {
        Assert.Null(WatchInstallOutcomes.FromWord(word));
        Assert.Null(WatchInstallOutcomes.StatusFromWord(word));
    }

    // ---- the record the watch keeps, and reports ----

    [Fact]
    public void OneResultPerApp()
    {
        IReadOnlyList<WatchInstallResult> results = [];
        results = InstallResults.Record(results, new WatchInstallResult(WatchIdentity.DashPackage, 7, WatchInstallOutcome.Waiting));
        results = InstallResults.Record(results, new WatchInstallResult(WatchIdentity.WavePackage, 3, WatchInstallOutcome.Installed));
        results = InstallResults.Record(results, new WatchInstallResult(WatchIdentity.DashPackage, 7, WatchInstallOutcome.InstalledAfterAsking));

        Assert.Equal(2, results.Count);
        Assert.Equal(WatchInstallOutcome.InstalledAfterAsking, results.Single(r => r.Package == WatchIdentity.DashPackage).Outcome);
    }

    [Fact]
    public void AnOlderResultDoesNotReplaceANewerOne()
    {
        // A newer build's prompt replaces the older one's session, whose abandonment can be reported afterwards.
        IReadOnlyList<WatchInstallResult> results = [new WatchInstallResult(WatchIdentity.DashPackage, 8, WatchInstallOutcome.Waiting)];

        results = InstallResults.Record(results, new WatchInstallResult(WatchIdentity.DashPackage, 7, WatchInstallOutcome.Declined));

        Assert.Equal(new WatchInstallResult(WatchIdentity.DashPackage, 8, WatchInstallOutcome.Waiting), Assert.Single(results));
    }

    [Fact]
    public void WaitingIsForTheSameVersion()
    {
        IReadOnlyList<WatchInstallResult> results = [new WatchInstallResult(WatchIdentity.DashPackage, 7, WatchInstallOutcome.Waiting)];

        Assert.True(InstallResults.WasWaiting(results, WatchIdentity.DashPackage, 7));
        Assert.False(InstallResults.WasWaiting(results, WatchIdentity.DashPackage, 8));
        Assert.False(InstallResults.WasWaiting(results, WatchIdentity.WavePackage, 7));
        Assert.False(InstallResults.WasWaiting([new WatchInstallResult(WatchIdentity.DashPackage, 7, WatchInstallOutcome.Failed)], WatchIdentity.DashPackage, 7));
    }

    [Fact]
    public void ResultsRoundTrip()
    {
        IReadOnlyList<WatchInstallResult> results =
        [
            new(WatchIdentity.DashPackage, 1_100_000_201, WatchInstallOutcome.Failed, InstallerStatus.Conflict),
            new(WatchIdentity.FacePackage, 1_100_000_202, WatchInstallOutcome.Installed),
        ];

        Assert.Equal(results, InstallResults.Decode(InstallResults.Encode(results)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"package\":1}")]
    [InlineData("[{\"package\":\"com.storyworthtelling.watch.point\",\"versionCode\":1,\"outcome\":\"sideways\"}]")]
    public void ADamagedRecordReadsEmpty(string? stored)
    {
        Assert.Empty(InstallResults.Decode(stored));
    }

    // ---- the log ----

    [Fact]
    public void TheLineIsTheOutcome()
    {
        Assert.Equal(
            "UPDATE com.storyworthtelling.focusphone.audio 1100000201: Installed",
            UpdateLines.Install(WatchIdentity.WavePackage, 1_100_000_201, WatchInstallOutcome.Installed));
    }

    [Fact]
    public void AFailureNamesItsStatus()
    {
        Assert.Equal(
            "UPDATE com.storyworthtelling.focusphone.audio 1100000201: Failed — conflict",
            UpdateLines.Install(WatchIdentity.WavePackage, 1_100_000_201, WatchInstallOutcome.Failed, InstallerStatus.Conflict));
    }

    [Fact]
    public void AnAskedSilentUpdateIsSaid()
    {
        // Risk R2, found on the first update: silence was asked for and the wrist was asked anyway.
        var line = UpdateLines.Install(WatchIdentity.WavePackage, 1_100_000_201, WatchInstallOutcome.Waiting, silenceRequested: true);

        Assert.Equal("UPDATE com.storyworthtelling.focusphone.audio 1100000201: Waiting — asked although silence was requested", line);
    }

    [Fact]
    public void TheLinesSayWordsAndNumbersOnly()
    {
        foreach (var outcome in Enum.GetValues<WatchInstallOutcome>())
        {
            foreach (var status in Enum.GetValues<InstallerStatus>())
            {
                var line = UpdateLines.Install(WatchIdentity.DashPackage, 1, outcome, status, silenceRequested: true);
                Assert.Matches(new Regex(@"^UPDATE [a-z.]+ \d+: [A-Za-z]+( — [a-z -]+)?$"), line);
            }
        }
    }

    [Fact]
    public void ACardThatCannotShowIsSaid()
    {
        // POST_NOTIFICATIONS is granted once, by `adb install -g`; without it the prompt waits where nobody can see it.
        Assert.Equal(
            "UPDATE com.storyworthtelling.focusphone.communication 7: Waiting — notifications are off, so nothing reached the wrist",
            UpdateLines.CannotAsk(WatchIdentity.DashPackage, 7));
    }

    [Fact]
    public void AReceiptSaysTheVersion()
    {
        var read = ContractRead<InstalledMessage>.Read(new InstalledAsk(), 1);

        Assert.Equal("CONTRACT update/installed: received v1", UpdateLines.Receipt(UpdateLines.InstalledContract, read));
    }

    [Fact]
    public void ANewerOneIsRefusedByVersion()
    {
        var read = ContractRead<ReceivedApk>.Refused(ContractRefusal.Newer, 2);

        Assert.Equal("CONTRACT update/apk: refused v2, newer than this watch reads", UpdateLines.Receipt(UpdateLines.ApkContract, read));
    }

    [Fact]
    public void AnUnreadableOneSaysSo()
    {
        var read = ContractRead<ReceivedApk>.Refused(ContractRefusal.Unreadable, 0);

        Assert.Equal("CONTRACT update/apk: refused, unreadable", UpdateLines.Receipt(UpdateLines.ApkContract, read));
    }

    [Fact]
    public void ASendSaysTheVersion()
    {
        Assert.Equal("CONTRACT update/installed: sent v1", UpdateLines.Sent(UpdateLines.InstalledContract));
        Assert.Equal("CONTRACT update/installed: not sent, ApiException", UpdateLines.NotSent(UpdateLines.InstalledContract, "ApiException"));
    }

    [Fact]
    public void ATransferThatDidNotFinishSaysHow()
    {
        Assert.Equal("CONTRACT update/apk: transfer ended, disconnected", UpdateLines.Transfer(UpdateLines.ApkContract, "disconnected"));
    }

    // ---- the one thing the wrist is asked ----

    [Theory]
    [InlineData(WatchIdentity.DashPackage, WatchUpdateAction.FirstInstall, "Tap to install Dash.")]
    [InlineData(WatchIdentity.WavePackage, WatchUpdateAction.FirstInstall, "Tap to install Wave.")]
    [InlineData(WatchIdentity.BeatPackage, WatchUpdateAction.FirstInstall, "Tap to install Beat.")]
    [InlineData(WatchIdentity.FacePackage, WatchUpdateAction.FirstInstall, "Tap to install Point.")]
    [InlineData(WatchIdentity.DashPackage, WatchUpdateAction.AskedUpdate, "Tap to update Dash.")]
    [InlineData(WatchIdentity.InstallerPackage, WatchUpdateAction.SilentUpdate, "Tap to update Point installer.")]
    [InlineData("com.storyworthtelling.watch.later", WatchUpdateAction.FirstInstall, "Tap to install a watch app.")]
    public void TheWristIsAskedOneSentence(string package, WatchUpdateAction action, string sentence)
    {
        Assert.Equal(sentence, InstallPrompt.Sentence(package, action));
        Assert.Equal(WatchTextVerdict.Fits, WatchText.Check(sentence));
    }

    [Fact]
    public void TheInstallersOwnWords()
    {
        Assert.Equal("Point installer", InstallPrompt.InstallerLabel);
        Assert.Equal(InstallPrompt.InstallerLabel, InstallPrompt.NameOf(WatchIdentity.InstallerPackage));
        Assert.Equal("Installs", InstallPrompt.ChannelName);
    }

    [Fact]
    public void EachAppHasItsOwnPrompt()
    {
        var ids = WatchIdentity.Packages.Select(InstallPrompt.NotificationId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id > 0));
        Assert.DoesNotContain(InstallPrompt.NotificationId("com.storyworthtelling.watch.later"), ids);
    }
}
