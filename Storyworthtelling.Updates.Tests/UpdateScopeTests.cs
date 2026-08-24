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
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher,
            [new InstallCandidate(FutureHealthApp, 1)], Installed());

        Assert.Contains(plan, c => c.Package == FutureHealthApp);
    }

    [Fact]
    public void Custodian_UpdatesAnInstalledOwnerApp()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher,
            [new InstallCandidate(Storyboarder, 42)], Installed((Storyboarder, 41)));

        Assert.Contains(plan, c => c.Package == Storyboarder);
    }

    [Fact]
    public void Custodian_SkipsAVersionTheDeviceAlreadyHas()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher,
            [new InstallCandidate(Storyboarder, 42)], Installed((Storyboarder, 42)));

        Assert.Empty(plan);
    }

    [Fact]
    public void Custodian_NeverDowngrades()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher,
            [new InstallCandidate(Launcher, 5)], Installed((Launcher, 9)));

        Assert.Empty(plan);
    }

    [Fact]
    public void Custodian_InstallsOnlyTheOwnerAppsThatAdvanced()
    {
        var plan = UpdateScope.Decide(UpdateRole.Custodian, Launcher,
            [
                new InstallCandidate(Launcher, 10),        // newer -> install
                new InstallCandidate(Storyboarder, 3),     // same -> skip
                new InstallCandidate(FutureHealthApp, 1),  // new -> install
                new InstallCandidate("com.evil.app", 100), // not owner -> skip
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
        var plan = UpdateScope.Decide(UpdateRole.SelfOnly, Score,
            [new InstallCandidate(Score, 2)], Installed((Score, 1)));

        Assert.Equal([Score], plan.Select(c => c.Package));
    }

    [Fact]
    public void SelfOnly_LeavesEveryOtherAppAlone()
    {
        // The whole difference between the roles: another app's update is that app's
        // business, even when this run is looking straight at its APK.
        var plan = UpdateScope.Decide(UpdateRole.SelfOnly, Score,
            [new InstallCandidate(Launcher, 9), new InstallCandidate(FutureHealthApp, 3)],
            Installed((Launcher, 1)));

        Assert.Empty(plan);
    }

    [Fact]
    public void SelfOnly_SkipsABuildItAlreadyHas()
    {
        var plan = UpdateScope.Decide(UpdateRole.SelfOnly, Score,
            [new InstallCandidate(Score, 5)], Installed((Score, 5)));

        Assert.Empty(plan);
    }

    // ---- The trust boundary, which is not a device question ----

    [Fact]
    public void NeitherRoleTouchesAPackageOutsideTheSuite()
    {
        var stray = new InstallCandidate("com.someone.else", 99);

        Assert.Empty(UpdateScope.Decide(UpdateRole.Custodian, Launcher, [stray], Installed()));
        Assert.Empty(UpdateScope.Decide(UpdateRole.SelfOnly, "com.someone.else", [stray], Installed()));
    }

    [Fact]
    public void OwnerPrefixIsTheSuiteNamespace()
    {
        Assert.Equal("com.storyworthtelling.", SuiteApps.OwnerPackagePrefix);
    }
}
