namespace Storyworthtelling.Updates.Tests;

public class UpdateScopeTests
{
    private const string Launcher = "com.storyworthtelling.focusphone.launcher";
    private const string Score = "com.storyworthtelling.focusphone.audio";
    private const string Storyboarder = "com.storyworthtelling.storyboarder";
    private const string FutureHealthApp = "com.storyworthtelling.health";

    private static Dictionary<string, long> Installed(params (string Package, long Version)[] apps) =>
        apps.ToDictionary(a => a.Package, a => a.Version);

    // ---- The custodian: the device-owner launcher on the locked phone ----

    [Fact]
    public void Custodian_InstallsANewOwnerAppOnFirstSight()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher, ApkForm.Phone,
            [new InstallCandidate(FutureHealthApp, 1, ApkForm.Phone)], Installed());

        Assert.Contains(plan, c => c.Package == FutureHealthApp);
    }

    [Fact]
    public void Custodian_UpdatesAnInstalledOwnerApp()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher, ApkForm.Phone,
            [new InstallCandidate(Storyboarder, 42, ApkForm.Phone)], Installed((Storyboarder, 41)));

        Assert.Contains(plan, c => c.Package == Storyboarder);
    }

    [Fact]
    public void Custodian_SkipsAVersionTheDeviceAlreadyHas()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher, ApkForm.Phone,
            [new InstallCandidate(Storyboarder, 42, ApkForm.Phone)], Installed((Storyboarder, 42)));

        Assert.Empty(plan);
    }

    [Fact]
    public void Custodian_NeverDowngrades()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher, ApkForm.Phone,
            [new InstallCandidate(Launcher, 5, ApkForm.Phone)], Installed((Launcher, 9)));

        Assert.Empty(plan);
    }

    [Fact]
    public void Custodian_InstallsOnlyTheOwnerAppsThatAdvanced()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher, ApkForm.Phone,
            [
                new InstallCandidate(Launcher, 10, ApkForm.Phone),        // newer -> install
                new InstallCandidate(Storyboarder, 3, ApkForm.Phone),     // same -> skip
                new InstallCandidate(FutureHealthApp, 1, ApkForm.Phone),  // new -> install
                new InstallCandidate("com.evil.app", 100, ApkForm.Phone), // not owner -> skip
            ],
            Installed((Launcher, 9), (Storyboarder, 3)));

        Assert.Equal(
            [Launcher, FutureHealthApp],
            plan.Select(c => c.Package).OrderBy(p => p).ToArray());
    }

    // ---- Self-only: an app on an ordinary phone, installing for itself ----

    [Fact]
    public void SelfOnly_TakesANewerBuildOfItself()
    {
        var plan = UpdateScope.Decide(UpdateRole.SelfOnly, Score, ApkForm.Phone,
            [new InstallCandidate(Score, 2, ApkForm.Phone)], Installed((Score, 1)));

        Assert.Equal([Score], plan.Select(c => c.Package));
    }

    [Fact]
    public void SelfOnly_LeavesEveryOtherAppAlone()
    {
        // The whole difference between the roles: another app's update is that app's
        // business, even when this run is looking straight at its APK.
        var plan = UpdateScope.Decide(UpdateRole.SelfOnly, Score, ApkForm.Phone,
            [new InstallCandidate(Launcher, 9, ApkForm.Phone), new InstallCandidate(FutureHealthApp, 3, ApkForm.Phone)],
            Installed((Launcher, 1)));

        Assert.Empty(plan);
    }

    [Fact]
    public void SelfOnly_SkipsABuildItAlreadyHas()
    {
        var plan = UpdateScope.Decide(UpdateRole.SelfOnly, Score, ApkForm.Phone,
            [new InstallCandidate(Score, 5, ApkForm.Phone)], Installed((Score, 5)));

        Assert.Empty(plan);
    }

    // ---- The trust boundary, which is not a device question ----

    [Fact]
    public void NeitherRoleTouchesAPackageOutsideTheSuite()
    {
        var stray = new InstallCandidate("com.someone.else", 99, ApkForm.Phone);

        Assert.Empty(UpdateScope.Decide(UpdateRole.Custodian, Launcher, ApkForm.Phone, [stray], Installed()));
        Assert.Empty(UpdateScope.Decide(UpdateRole.SelfOnly, "com.someone.else", ApkForm.Phone, [stray], Installed()));
    }

    [Fact]
    public void OwnerPrefixIsTheSuiteNamespace()
    {
        Assert.Equal("com.storyworthtelling.", SuiteApps.OwnerPackagePrefix);
    }
}
