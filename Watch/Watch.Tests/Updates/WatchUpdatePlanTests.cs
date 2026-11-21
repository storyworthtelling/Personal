using Storyworthtelling.Updates;
using Watch.Domain;
using Watch.Domain.Updates;

namespace Watch.Tests.Updates;

/// <summary>
/// What the Point installer does with an APK the phone offers (P14 W-3.1.2): refuse it, install it for the first time
/// (the wrist asks once), or update it silently as its installer of record. A watch app carries its phone app's package,
/// so the form is judged before anything else.
/// </summary>
public class WatchUpdatePlanTests
{
    private const long Band11 = 1_100_000_200;

    private static InstallCandidate Watch(string package, long code = Band11) => new(package, code, ApkForm.Watch);

    private static InstalledApp Have(string package, long code, bool ofRecord) => new(package, code, ofRecord);

    [Fact]
    public void FirstInstallAsksOnce()
    {
        var decision = WatchUpdatePlan.Decide(Watch(WatchIdentity.DashPackage), []);

        Assert.Equal(WatchUpdateAction.FirstInstall, decision.Action);
        Assert.True(decision.Install);
        Assert.True(decision.AsksTheWrist);
        Assert.False(decision.RequestsSilence);
        Assert.Null(decision.Refusal);
    }

    [Fact]
    public void UpdateOfRecordIsSilent()
    {
        var decision = WatchUpdatePlan.Decide(
            Watch(WatchIdentity.WavePackage, Band11 + 1),
            [Have(WatchIdentity.WavePackage, Band11, ofRecord: true)]);

        Assert.Equal(WatchUpdateAction.SilentUpdate, decision.Action);
        Assert.True(decision.RequestsSilence);
        Assert.False(decision.AsksTheWrist);
    }

    [Fact]
    public void UpdateOfAnotherInstallerAsks()
    {
        // Installed by hand with adb: this installer is not its installer of record, so the platform asks, once.
        var decision = WatchUpdatePlan.Decide(
            Watch(WatchIdentity.BeatPackage, Band11 + 1),
            [Have(WatchIdentity.BeatPackage, Band11, ofRecord: false)]);

        Assert.Equal(WatchUpdateAction.AskedUpdate, decision.Action);
        Assert.True(decision.AsksTheWrist);
        Assert.False(decision.RequestsSilence);
    }

    [Fact]
    public void TheInstallerUpdatesItselfSilently()
    {
        // Installed once with adb (A78), so never its own installer of record; an app updating itself needs no one's say.
        var decision = WatchUpdatePlan.Decide(
            Watch(WatchIdentity.InstallerPackage, Band11 + 1),
            [Have(WatchIdentity.InstallerPackage, Band11, ofRecord: false)]);

        Assert.Equal(WatchUpdateAction.SilentUpdate, decision.Action);
    }

    [Fact]
    public void TheFaceInstallsLikeAnApp()
    {
        Assert.Equal(WatchUpdateAction.FirstInstall, WatchUpdatePlan.Decide(Watch(WatchIdentity.FacePackage), []).Action);
    }

    [Theory]
    [InlineData(WatchIdentity.DashPackage)]
    [InlineData(WatchIdentity.WavePackage)]
    [InlineData(WatchIdentity.BeatPackage)]
    [InlineData(WatchIdentity.InstallerPackage)]
    public void TheWatchNeverInstallsAPhoneApk(string package)
    {
        // Post, Score, Constitution and Flyleaf carry the same packages as the watch apps; on the watch a phone APK is
        // "not installed yet", the one reading that would install it.
        var decision = WatchUpdatePlan.Decide(new InstallCandidate(package, Band11 + 5, ApkForm.Phone), []);

        Assert.Equal(WatchUpdateAction.Refuse, decision.Action);
        Assert.False(decision.Install);
        Assert.Equal(WatchInstallOutcome.NotForThisWatch, decision.Refusal);
    }

    [Fact]
    public void AnUnknownFormIsRefused()
    {
        var decision = WatchUpdatePlan.Decide(new InstallCandidate(WatchIdentity.DashPackage, Band11, ApkForm.Unknown), []);

        Assert.Equal(WatchInstallOutcome.NotForThisWatch, decision.Refusal);
    }

    [Theory]
    [InlineData("com.example.probe")]
    [InlineData("com.storyworthtellingx.probe")]
    [InlineData("")]
    public void OnlyTheSuite(string package)
    {
        var decision = WatchUpdatePlan.Decide(Watch(package), []);

        Assert.Equal(WatchUpdateAction.Refuse, decision.Action);
        Assert.Equal(WatchInstallOutcome.NotOurs, decision.Refusal);
    }

    [Fact]
    public void TheFormComesFirst()
    {
        var decision = WatchUpdatePlan.Decide(new InstallCandidate("com.example.probe", 1, ApkForm.Phone), []);

        Assert.Equal(WatchInstallOutcome.NotForThisWatch, decision.Refusal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NotNewerIsCurrent(long offset)
    {
        var decision = WatchUpdatePlan.Decide(
            Watch(WatchIdentity.DashPackage, Band11 + offset),
            [Have(WatchIdentity.DashPackage, Band11, ofRecord: true)]);

        Assert.Equal(WatchUpdateAction.Refuse, decision.Action);
        Assert.Equal(WatchInstallOutcome.AlreadyCurrent, decision.Refusal);
    }

    [Fact]
    public void OtherAppsDoNotCount()
    {
        var decision = WatchUpdatePlan.Decide(
            Watch(WatchIdentity.DashPackage, Band11),
            [Have(WatchIdentity.WavePackage, Band11 + 9, ofRecord: true)]);

        Assert.Equal(WatchUpdateAction.FirstInstall, decision.Action);
    }

    // ---- after the bytes have arrived: the APK's own readings decide ----

    private static ReceivedApk Received(string package = WatchIdentity.DashPackage, long code = Band11, ApkForm form = ApkForm.Watch, bool intact = true) =>
        new(new UpdateApkHeader(package, code, form, 1024, new string('a', 64)), intact);

    private static ApkReading Read(string? package = WatchIdentity.DashPackage, long code = Band11, ApkForm scanned = ApkForm.Watch, ApkForm declared = ApkForm.Watch) =>
        new(package, code, scanned, declared);

    [Fact]
    public void VerifyInstallsWhatAgrees()
    {
        var decision = WatchUpdatePlan.Verify(Received(), Read(), []);

        Assert.Equal(WatchUpdateAction.FirstInstall, decision.Action);
    }

    [Fact]
    public void ADamagedTransferIsRefused()
    {
        Assert.Equal(WatchInstallOutcome.Damaged, WatchUpdatePlan.Verify(Received(intact: false), Read(), []).Refusal);
    }

    [Fact]
    public void WhatThePlatformCannotReadIsRefused()
    {
        Assert.Equal(WatchInstallOutcome.Damaged, WatchUpdatePlan.Verify(Received(), Read(package: null), []).Refusal);
    }

    [Fact]
    public void AnotherPackageIsRefused()
    {
        // The bytes are not what the phone said they were: neither is trusted.
        Assert.Equal(WatchInstallOutcome.Damaged, WatchUpdatePlan.Verify(Received(), Read(package: WatchIdentity.WavePackage), []).Refusal);
    }

    [Fact]
    public void AnotherVersionIsRefused()
    {
        Assert.Equal(WatchInstallOutcome.Damaged, WatchUpdatePlan.Verify(Received(), Read(code: Band11 + 1), []).Refusal);
    }

    [Fact]
    public void APhoneApkCalledAWatchApkIsRefused()
    {
        // The installer's own refusal: whatever the header says, a manifest without the watch feature is a phone APK.
        var decision = WatchUpdatePlan.Verify(Received(), Read(scanned: ApkForm.Phone, declared: ApkForm.Phone), []);

        Assert.Equal(WatchUpdateAction.Refuse, decision.Action);
        Assert.Equal(WatchInstallOutcome.NotForThisWatch, decision.Refusal);
    }

    [Theory]
    [InlineData(ApkForm.Watch, ApkForm.Phone)]
    [InlineData(ApkForm.Phone, ApkForm.Watch)]
    [InlineData(ApkForm.Unknown, ApkForm.Watch)]
    public void ReadingsMustAgree(ApkForm scanned, ApkForm declared)
    {
        Assert.Equal(WatchInstallOutcome.NotForThisWatch, WatchUpdatePlan.Verify(Received(), Read(scanned: scanned, declared: declared), []).Refusal);
    }

    [Fact]
    public void AHeaderThatSaysPhoneIsRefused()
    {
        Assert.Equal(WatchInstallOutcome.NotForThisWatch, WatchUpdatePlan.Verify(Received(form: ApkForm.Phone), Read(), []).Refusal);
    }

    [Fact]
    public void VerifyStillComparesVersions()
    {
        var decision = WatchUpdatePlan.Verify(Received(), Read(), [Have(WatchIdentity.DashPackage, Band11, ofRecord: true)]);

        Assert.Equal(WatchInstallOutcome.AlreadyCurrent, decision.Refusal);
    }

    [Fact]
    public void VerifyKeepsTheSilentRule()
    {
        var decision = WatchUpdatePlan.Verify(Received(), Read(), [Have(WatchIdentity.DashPackage, Band11 - 1, ofRecord: true)]);

        Assert.Equal(WatchUpdateAction.SilentUpdate, decision.Action);
    }
}
