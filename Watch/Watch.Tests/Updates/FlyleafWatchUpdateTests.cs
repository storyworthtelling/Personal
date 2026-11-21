using Storyworthtelling.Updates;
using Watch.Domain;
using Watch.Domain.Updates;

namespace Watch.Tests.Updates;

/// <summary>
/// Flyleaf's half of the watch's update channel (P14 W-3.1.3): which of the release's watch APKs it sends, when it holds
/// back, the record it keeps of what it sent, and the one sentence Settings says about the watch's last run. The
/// version codes are invented, in band 11.
/// </summary>
public class FlyleafWatchUpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private static ReleaseApk Watch(long assetId, string package, long versionCode) =>
        new(assetId, new InstallCandidate(package, versionCode, ApkForm.Watch));

    private static ReleaseApk Phone(long assetId, string package, long versionCode) =>
        new(assetId, new InstallCandidate(package, versionCode, ApkForm.Phone));

    private static WatchAnswer Has(params WatchApp[] apps) => WatchAnswer.Answered(new InstalledReport(apps, []));

    private static WatchAnswer Has(WatchApp[] apps, params WatchInstallResult[] results) =>
        WatchAnswer.Answered(new InstalledReport(apps, results));

    private static WatchSendVerdict VerdictFor(IReadOnlyList<WatchSendDecision> decisions, string package) =>
        decisions.Single(decision => decision.Apk.Candidate.Package == package).Verdict;

    // ---- what is sent ----

    [Fact]
    public void SendsOnlyNewer()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005), Watch(2, WatchIdentity.WavePackage, 1100000007)],
            Has(new WatchApp(WatchIdentity.DashPackage, 1100000004), new WatchApp(WatchIdentity.WavePackage, 1100000007)),
            [], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.DashPackage));
        Assert.Equal(WatchSendVerdict.AlreadyCurrent, VerdictFor(decisions, WatchIdentity.WavePackage));
    }

    [Fact]
    public void AnOlderBuildIsNotSent()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000003)], Has(new WatchApp(WatchIdentity.DashPackage, 1100000004)), [], Now);

        Assert.Equal(WatchSendVerdict.AlreadyCurrent, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void WhatIsNotThereIsSent()
    {
        var decisions = WatchSendPlan.Decide([Watch(1, WatchIdentity.BeatPackage, 1100000001)], Has(), [], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.BeatPackage));
    }

    [Fact]
    public void APhoneApkIsNeverSent()
    {
        // Post's own APK carries Dash's package; the watch has no Dash, which would read as "send it".
        var decisions = WatchSendPlan.Decide(
            [Phone(1, WatchIdentity.DashPackage, 100000009), new ReleaseApk(2, new InstallCandidate(WatchIdentity.WavePackage, 1100000002, ApkForm.Unknown))],
            Has(), [], Now);

        Assert.Empty(decisions);
    }

    [Fact]
    public void OnlyTheSuite()
    {
        var decisions = WatchSendPlan.Decide([Watch(1, "com.example.watchface", 5)], Has(), [], Now);

        Assert.Empty(decisions);
    }

    [Fact]
    public void TheFaceIsSentLikeAnApp()
    {
        var decisions = WatchSendPlan.Decide([Watch(1, WatchIdentity.FacePackage, 1100000002)], Has(), [], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.FacePackage));
    }

    [Fact]
    public void TheInstallerMayUpdateItself()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.InstallerPackage, 1100000009)], Has(new WatchApp(WatchIdentity.InstallerPackage, 1100000008)), [], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.InstallerPackage));
    }

    [Fact]
    public void TheInstallerGoesLast()
    {
        // Replacing itself ends the installer's process; anything sent after it would land on nobody.
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.InstallerPackage, 1100000009), Watch(2, WatchIdentity.DashPackage, 1100000005), Watch(3, WatchIdentity.FacePackage, 1100000002)],
            Has(new WatchApp(WatchIdentity.InstallerPackage, 1100000008)), [], Now);

        Assert.Equal([2L, 3L, 1L], decisions.Where(decision => decision.Send).Select(decision => decision.Apk.AssetId));
    }

    // ---- waiting for the watch ----

    [Theory]
    [InlineData(WatchReach.NoWatch)]
    [InlineData(WatchReach.Unreachable)]
    [InlineData(WatchReach.NoAnswer)]
    [InlineData(WatchReach.Unreadable)]
    [InlineData(WatchReach.Newer)]
    public void WaitsForTheWatch(WatchReach reach)
    {
        var decisions = WatchSendPlan.Decide([Watch(1, WatchIdentity.DashPackage, 1100000005)], WatchAnswer.Not(reach), [], Now);

        Assert.Empty(decisions);
    }

    [Fact]
    public void AnAnswerNeedsAReport()
    {
        Assert.Throws<ArgumentException>(() => WatchAnswer.Not(WatchReach.Answered));
        Assert.Equal(WatchReach.Answered, Has().Reach);
        Assert.Null(WatchAnswer.Not(WatchReach.NoAnswer).Report);
    }

    // ---- the sent-record ----

    [Fact]
    public void ASendStillLandingIsNotSentAgain()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)], Has(),
            [new SentApk(WatchIdentity.DashPackage, 1100000005, Now.AddMinutes(-30))], Now);

        Assert.Equal(WatchSendVerdict.InFlight, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void ALostSendIsSentAgain()
    {
        // A channel that dropped mid-transfer leaves no result on the watch: an hour with no word is a lost send.
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)], Has(),
            [new SentApk(WatchIdentity.DashPackage, 1100000005, Now - WatchSendPlan.InFlightWait)], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void AnOlderSendDoesNotHoldANewerBuild()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)], Has(),
            [new SentApk(WatchIdentity.DashPackage, 1100000004, Now.AddMinutes(-5))], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void AResultForAnotherBuildDoesNotCount()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000004, WatchInstallOutcome.Failed, InstallerStatus.Conflict)),
            [new SentApk(WatchIdentity.DashPackage, 1100000005, Now.AddMinutes(-5))], Now);

        Assert.Equal(WatchSendVerdict.InFlight, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void AWaitingCardIsLeftAlone()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.Waiting)),
            [new SentApk(WatchIdentity.DashPackage, 1100000005, Now.AddDays(-1))], Now);

        Assert.Equal(WatchSendVerdict.WaitingOnTheWrist, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void ACardLeftUnansweredIsOfferedAgain()
    {
        // The platform drops an unanswered session after three days, so after that there is nothing left to tap.
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.Waiting)),
            [new SentApk(WatchIdentity.DashPackage, 1100000005, Now - WatchSendPlan.WaitingOffer)], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void AWaitingCardWithNoRecordIsOfferedAgain()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.Waiting)), [], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void ADeclinedBuildIsNotSentAgain()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.Declined)),
            [new SentApk(WatchIdentity.DashPackage, 1100000005, Now.AddDays(-10))], Now);

        Assert.Equal(WatchSendVerdict.Declined, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void ANewerBuildFollowsADecline()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000004, WatchInstallOutcome.Declined)), [], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Theory]
    [InlineData(WatchInstallOutcome.Failed)]
    [InlineData(WatchInstallOutcome.Damaged)]
    public void AFailureIsSentAgain(WatchInstallOutcome outcome)
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, outcome)),
            [new SentApk(WatchIdentity.DashPackage, 1100000005, Now.AddMinutes(-5))], Now);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Theory]
    [InlineData(WatchInstallOutcome.NotForThisWatch)]
    [InlineData(WatchInstallOutcome.NotOurs)]
    public void ARefusalIsNotSentAgain(WatchInstallOutcome outcome)
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, outcome)), [], Now);

        Assert.Equal(WatchSendVerdict.RefusedByTheWatch, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Theory]
    [InlineData(WatchInstallOutcome.Installed)]
    [InlineData(WatchInstallOutcome.InstalledAfterAsking)]
    [InlineData(WatchInstallOutcome.AlreadyCurrent)]
    public void AReportedInstallCounts(WatchInstallOutcome outcome)
    {
        // Removed from the watch by hand afterwards, or hidden from the installer's view: it isn't pushed back on.
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005)],
            Has([], new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, outcome)), [], Now);

        Assert.Equal(WatchSendVerdict.AlreadyCurrent, VerdictFor(decisions, WatchIdentity.DashPackage));
    }

    [Fact]
    public void TheOwnerCanAskAgain()
    {
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005), Watch(2, WatchIdentity.WavePackage, 1100000007), Watch(3, WatchIdentity.BeatPackage, 1100000002)],
            Has([],
                new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.Declined),
                new WatchInstallResult(WatchIdentity.WavePackage, 1100000007, WatchInstallOutcome.Waiting)),
            [new SentApk(WatchIdentity.WavePackage, 1100000007, Now.AddHours(-2)), new SentApk(WatchIdentity.BeatPackage, 1100000002, Now.AddMinutes(-10))],
            Now, force: true);

        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.DashPackage));
        Assert.Equal(WatchSendVerdict.Send, VerdictFor(decisions, WatchIdentity.WavePackage));
        // A send still landing is not doubled, even when asked.
        Assert.Equal(WatchSendVerdict.InFlight, VerdictFor(decisions, WatchIdentity.BeatPackage));
    }

    [Fact]
    public void TheRecordKeepsOneSendPerApp()
    {
        var record = WatchSentRecord.Record([], new SentApk(WatchIdentity.DashPackage, 1100000004, Now.AddDays(-1)));
        record = WatchSentRecord.Record(record, new SentApk(WatchIdentity.WavePackage, 1100000007, Now.AddHours(-1)));
        record = WatchSentRecord.Record(record, new SentApk(WatchIdentity.DashPackage, 1100000005, Now));

        Assert.Equal(2, record.Count);
        Assert.Contains(new SentApk(WatchIdentity.DashPackage, 1100000005, Now), record);
        Assert.Contains(new SentApk(WatchIdentity.WavePackage, 1100000007, Now.AddHours(-1)), record);
    }

    [Fact]
    public void TheRecordRoundTrips()
    {
        IReadOnlyList<SentApk> record =
        [
            new SentApk(WatchIdentity.DashPackage, 1100000005, Now),
            new SentApk(WatchIdentity.InstallerPackage, 1100000009, Now.AddMinutes(-3)),
        ];

        Assert.Equal(record, WatchSentRecord.Decode(WatchSentRecord.Encode(record)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[{\"package\": 5}]")]
    [InlineData("[{\"package\": \"com.storyworthtelling.constitution\", \"versionCode\": 1}]")]
    public void ADamagedRecordReadsEmpty(string? stored)
    {
        Assert.Empty(WatchSentRecord.Decode(stored));
    }

    [Fact]
    public void ATransferHasItsOwnName()
    {
        var first = WatchSendPlan.TransferName(9_000_000_123, Guid.NewGuid());
        var second = WatchSendPlan.TransferName(9_000_000_123, Guid.NewGuid());

        Assert.NotEqual(first, second);
        Assert.Equal(first, UpdatePaths.TransferOf(UpdatePaths.ApkPathFor(first)));
        Assert.StartsWith("9000000123-", first, StringComparison.Ordinal);
    }

    // ---- Settings' quiet line ----

    private static readonly WatchApp DashNew = new(WatchIdentity.DashPackage, 1100000005);
    private static readonly WatchApp WaveNew = new(WatchIdentity.WavePackage, 1100000007);
    private static readonly WatchApp BeatNew = new(WatchIdentity.BeatPackage, 1100000002);

    private static WatchRunRecord Ran(IReadOnlyList<WatchApp> due, IReadOnlyList<WatchApp> sent, InstalledReport report, IReadOnlyList<WatchApp>? notSent = null) =>
        new(Now.AddHours(-2), WatchReach.Answered, due, sent, notSent ?? [], report);

    private static string? Say(WatchRunRecord? run) => WatchUpdateLine.Say(run, Now, TimeZoneInfo.Utc);

    [Fact]
    public void NoRunSaysNothing()
    {
        Assert.Null(Say(null));
    }

    [Fact]
    public void NoWatchSaysNothing()
    {
        Assert.Null(Say(new WatchRunRecord(Now.AddHours(-2), WatchReach.NoWatch, [], [], [], null)));
    }

    [Theory]
    [InlineData(WatchReach.Unreachable, "Watch checked 2h ago: it was out of reach, so nothing was sent.")]
    [InlineData(WatchReach.NoAnswer, "Watch checked 2h ago: it didn’t answer, so nothing was sent.")]
    [InlineData(WatchReach.Unreadable, "Watch checked 2h ago: its answer couldn’t be read, so nothing was sent.")]
    [InlineData(WatchReach.Newer, "Watch checked 2h ago: its installer is newer than Flyleaf, so nothing was sent.")]
    public void SaysWhyNothingWent(WatchReach reach, string expected)
    {
        Assert.Equal(expected, Say(new WatchRunRecord(Now.AddHours(-2), reach, [], [], [], null)));
    }

    [Fact]
    public void SaysHowItWent()
    {
        var report = new InstalledReport(
            [DashNew],
            [
                new WatchInstallResult(WatchIdentity.WavePackage, 1100000007, WatchInstallOutcome.Failed, InstallerStatus.Conflict),
                new WatchInstallResult(WatchIdentity.BeatPackage, 1100000002, WatchInstallOutcome.Waiting),
            ]);

        Assert.Equal(
            "Watch checked 2h ago: Wave didn’t install (conflict), Beat is waiting for a tap and Dash installed.",
            Say(Ran([DashNew, WaveNew, BeatNew], [DashNew, WaveNew, BeatNew], report)));
    }

    [Fact]
    public void NothingDueIsUpToDate()
    {
        Assert.Equal("Watch checked 2h ago: everything is up to date.", Say(Ran([], [], new InstalledReport([DashNew], []))));
    }

    [Fact]
    public void ManyAreCounted()
    {
        var report = new InstalledReport([DashNew, WaveNew], []);

        Assert.Equal("Watch checked 2h ago: 2 apps installed.", Say(Ran([DashNew, WaveNew], [DashNew, WaveNew], report)));
    }

    [Fact]
    public void ManyWaitingAreCounted()
    {
        var report = new InstalledReport([],
        [
            new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.Waiting),
            new WatchInstallResult(WatchIdentity.WavePackage, 1100000007, WatchInstallOutcome.Waiting),
        ]);

        Assert.Equal("Watch checked 2h ago: 2 apps are waiting for a tap.", Say(Ran([DashNew, WaveNew], [DashNew, WaveNew], report)));
    }

    [Fact]
    public void ASendNotYetReportedIsOnItsWay()
    {
        Assert.Equal("Watch checked 2h ago: Dash is on its way.", Say(Ran([DashNew], [DashNew], new InstalledReport([], []))));
    }

    [Fact]
    public void ASendThatFailedOnThePhoneDidNotInstall()
    {
        Assert.Equal("Watch checked 2h ago: Dash didn’t install.", Say(Ran([DashNew], [], new InstalledReport([], []), notSent: [DashNew])));
    }

    [Fact]
    public void ADeclineIsSaid()
    {
        var report = new InstalledReport([], [new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.Declined)]);

        Assert.Equal("Watch checked 2h ago: Dash was turned down.", Say(Ran([DashNew], [], report)));
    }

    [Fact]
    public void ALaterReportUpdatesTheLine()
    {
        var run = Ran([DashNew], [DashNew], new InstalledReport([], []));

        var later = run.WithReport(new InstalledReport([DashNew], [new WatchInstallResult(WatchIdentity.DashPackage, 1100000005, WatchInstallOutcome.InstalledAfterAsking)]));

        Assert.Equal("Watch checked 2h ago: Dash installed.", Say(later));
        Assert.Equal(run.At, later.At);
        Assert.Equal(WatchReach.Answered, later.Reach);
    }

    [Fact]
    public void TheRunKeepsWhatWasDue()
    {
        var answer = Has(new WatchApp(WatchIdentity.WavePackage, 1100000007));
        var decisions = WatchSendPlan.Decide(
            [Watch(1, WatchIdentity.DashPackage, 1100000005), Watch(2, WatchIdentity.WavePackage, 1100000007)], answer, [], Now);

        var run = WatchRunRecord.Of(Now, answer, decisions, [DashNew], []);

        Assert.Equal([DashNew], run.Due);
        Assert.Equal([DashNew], run.Sent);
        Assert.Same(answer.Report, run.Report);
        Assert.Equal(WatchReach.Answered, run.Reach);
    }

    [Fact]
    public void TheRunRoundTrips()
    {
        var report = new InstalledReport([DashNew], [new WatchInstallResult(WatchIdentity.WavePackage, 1100000007, WatchInstallOutcome.Failed, InstallerStatus.Storage)]);
        var run = new WatchRunRecord(Now, WatchReach.Answered, [DashNew, WaveNew], [DashNew], [WaveNew], report);

        var read = WatchRunCodec.Decode(WatchRunCodec.Encode(run));

        Assert.NotNull(read);
        Assert.Equal(run.At, read.At);
        Assert.Equal(run.Reach, read.Reach);
        Assert.Equal(run.Due, read.Due);
        Assert.Equal(run.Sent, read.Sent);
        Assert.Equal(run.NotSent, read.NotSent);
        Assert.Equal(report.Apps, read.Report!.Apps);
        Assert.Equal(report.Results, read.Report.Results);
    }

    [Fact]
    public void ARunWithNoReportRoundTrips()
    {
        var run = new WatchRunRecord(Now, WatchReach.Unreachable, [], [], [], null);

        var read = WatchRunCodec.Decode(WatchRunCodec.Encode(run));

        Assert.NotNull(read);
        Assert.Equal(WatchReach.Unreachable, read.Reach);
        Assert.Null(read.Report);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"reach\": \"answered\"}")]
    [InlineData("{\"at\": \"yesterday\", \"reach\": \"answered\", \"due\": [], \"sent\": [], \"notSent\": []}")]
    [InlineData("{\"at\": \"2026-11-21T15:00:00+00:00\", \"reach\": \"somewhere\", \"due\": [], \"sent\": [], \"notSent\": []}")]
    public void ADamagedRunReadsAsNone(string? stored)
    {
        Assert.Null(WatchRunCodec.Decode(stored));
    }

    // ---- the log ----

    [Fact]
    public void TheLogSaysOutcomesOnly()
    {
        var decision = new WatchSendDecision(Watch(1, WatchIdentity.DashPackage, 1100000005), WatchSendVerdict.WaitingOnTheWrist);

        Assert.Equal("WATCH com.storyworthtelling.focusphone.communication 1100000005: WaitingOnTheWrist", WatchSendLines.Decision(decision));
        Assert.Equal("WATCH run: Unreachable", WatchSendLines.Run(WatchReach.Unreachable));
        Assert.Equal("WATCH com.storyworthtelling.focusphone.communication 1100000005: not sent, IOException", WatchSendLines.NotSent(WatchIdentity.DashPackage, 1100000005, "IOException"));
        Assert.Equal("WATCH com.storyworthtelling.focusphone.communication 1100000005: left for the next run", WatchSendLines.LeftForNextRun(WatchIdentity.DashPackage, 1100000005));
    }

    [Fact]
    public void WhatThePhoneHeardIsLogged()
    {
        Assert.Equal("CONTRACT update/installed: received v1", WatchSendLines.Heard(ContractRead<InstalledMessage>.Read(new InstalledReport([], []), 1)));
        Assert.Equal("CONTRACT update/installed: refused v2, newer than Flyleaf reads", WatchSendLines.Heard(ContractRead<InstalledMessage>.Refused(ContractRefusal.Newer, 2)));
        Assert.Equal("CONTRACT update/installed: refused, unreadable", WatchSendLines.Heard(ContractRead<InstalledMessage>.Refused(ContractRefusal.Unreadable, 0)));
    }

    [Fact]
    public void TheWaitsAreTheDecidedOnes()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), WatchSendPlan.AnswerWait);
        Assert.Equal(TimeSpan.FromHours(1), WatchSendPlan.InFlightWait);
        Assert.Equal(TimeSpan.FromDays(3), WatchSendPlan.WaitingOffer);
        Assert.Equal(TimeSpan.FromSeconds(30), WatchSendPlan.DataLayerWait);
        Assert.Equal(TimeSpan.FromSeconds(10), WatchSendPlan.CloseWait);
        Assert.Equal(TimeSpan.FromMinutes(1), WatchSendPlan.ReportWait);
        Assert.Equal(TimeSpan.FromMinutes(3), WatchSendPlan.SendNeeds);
    }

    [Fact]
    public void ASendNeedsRoomForItsOwnWaits()
    {
        // Opening the channel and its stream, the installer's report and closing: a send started with less than this
        // left would be cut off by the deadline before its report could come.
        Assert.True(WatchSendPlan.SendNeeds >= (2 * WatchSendPlan.DataLayerWait) + WatchSendPlan.ReportWait + WatchSendPlan.CloseWait);
        Assert.True(WatchSendPlan.SendNeeds < RunDeadline.WorkerCeiling - RunDeadline.Margin);
    }

    [Fact]
    public void TheRunsTimeIsSaidInWordsOnly()
    {
        Assert.Equal("WATCH run: out of time before sending com.storyworthtelling.focusphone.communication 1100000005, so it waits for the next run",
            WatchSendLines.OutOfTime("sending com.storyworthtelling.focusphone.communication 1100000005"));
        Assert.Equal("WATCH run: another run held the gate until this run’s time was up, so nothing was looked for", WatchSendLines.Busy());
        Assert.Equal("WATCH run: GetCapability didn’t answer in time", WatchSendLines.TimedOut("GetCapability"));
    }
}
