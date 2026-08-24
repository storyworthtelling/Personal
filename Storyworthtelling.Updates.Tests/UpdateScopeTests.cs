using Storyworthtelling.Updates;

namespace Storyworthtelling.Updates.Tests;

public class UpdateScopeTests
{
    private const string Flyleaf = "com.storyworthtelling.focusphone.launcher";
    private const string Score = "com.storyworthtelling.focusphone.audio";
    private const string Health = "com.storyworthtelling.health";

    private static Dictionary<string, long> Installed(params (string Package, long Version)[] apps) =>
        apps.ToDictionary(a => a.Package, a => a.Version);

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
        // The point of the split: another app's update is that app's business, even
        // when this run happens to be looking at its APK.
        var plan = UpdateScope.Decide(UpdateRole.SelfOnly, Score,
            [new InstallCandidate(Flyleaf, 9), new InstallCandidate(Health, 3)],
            Installed((Flyleaf, 1)));

        Assert.Empty(plan);
    }

    [Fact]
    public void SelfOnly_SkipsABuildItAlreadyHas()
    {
        var plan = UpdateScope.Decide(UpdateRole.SelfOnly, Score,
            [new InstallCandidate(Score, 5)], Installed((Score, 5)));

        Assert.Empty(plan);
    }

    [Fact]
    public void Bootstrapper_TakesANewerBuildOfItself()
    {
        var plan = UpdateScope.Decide(UpdateRole.Bootstrapper, Flyleaf,
            [new InstallCandidate(Flyleaf, 2)], Installed((Flyleaf, 1)));

        Assert.Equal([Flyleaf], plan.Select(c => c.Package));
    }

    // The bootstrapper's other job, and the reason it exists: a brand-new sibling app
    // lands on its first build rather than waiting for someone to sideload it.
    [Fact]
    public void Bootstrapper_InstallsAnAppTheDeviceDoesNotHaveYet()
    {
        var plan = UpdateScope.Decide(UpdateRole.Bootstrapper, Flyleaf,
            [new InstallCandidate(Health, 1)], Installed((Flyleaf, 1)));

        Assert.Equal([Health], plan.Select(c => c.Package));
    }

    // The behaviour that changes with the split. An installed sibling keeps itself
    // current, so the bootstrapper must not also push updates at it — two installers
    // racing the same package is how a half-applied update happens.
    [Fact]
    public void Bootstrapper_DoesNotUpdateAnAppThatIsAlreadyInstalled()
    {
        var plan = UpdateScope.Decide(UpdateRole.Bootstrapper, Flyleaf,
            [new InstallCandidate(Score, 9)], Installed((Flyleaf, 1), (Score, 1)));

        Assert.Empty(plan);
    }

    [Fact]
    public void NeitherRoleTouchesAPackageOutsideTheSuite()
    {
        var stray = new InstallCandidate("com.example.other", 9);

        Assert.Empty(UpdateScope.Decide(UpdateRole.Bootstrapper, Flyleaf, [stray], Installed()));
        Assert.Empty(UpdateScope.Decide(UpdateRole.SelfOnly, "com.example.other", [stray], Installed()));
    }

    [Fact]
    public void OwnerPrefixIsTheSuiteNamespace()
    {
        Assert.Equal("com.storyworthtelling.", SuiteApps.OwnerPackagePrefix);
    }
}
