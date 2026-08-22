using Codex.Domain.Updates;
using Xunit;

namespace Codex.Tests.Updates;

public class SiblingUpdatePlanTests
{
    private const string Launcher = "com.storyworthtelling.focusphone.launcher";
    private const string Storyboarder = "com.storyworthtelling.storyboarder";
    private const string FutureHealthApp = "com.storyworthtelling.health";

    [Fact]
    public void NewOwnerApp_NotInstalled_IsInstalledOnFirstSight()
    {
        var plan = SiblingUpdatePlan.Decide(
            [new InstallCandidate(FutureHealthApp, 1)],
            new Dictionary<string, long>());

        Assert.Contains(plan, c => c.Package == FutureHealthApp);
    }

    [Fact]
    public void InstalledOwnerApp_NewerVersion_IsUpdated()
    {
        var plan = SiblingUpdatePlan.Decide(
            [new InstallCandidate(Storyboarder, 42)],
            new Dictionary<string, long> { [Storyboarder] = 41 });

        Assert.Contains(plan, c => c.Package == Storyboarder);
    }

    [Fact]
    public void InstalledOwnerApp_SameVersion_IsSkipped()
    {
        var plan = SiblingUpdatePlan.Decide(
            [new InstallCandidate(Storyboarder, 42)],
            new Dictionary<string, long> { [Storyboarder] = 42 });

        Assert.Empty(plan);
    }

    [Fact]
    public void InstalledOwnerApp_OlderVersion_IsSkipped_NoDowngrade()
    {
        var plan = SiblingUpdatePlan.Decide(
            [new InstallCandidate(Launcher, 5)],
            new Dictionary<string, long> { [Launcher] = 9 });

        Assert.Empty(plan);
    }

    [Fact]
    public void NonOwnerPackage_IsNeverInstalled_EvenIfAbsentAndNewer()
    {
        var plan = SiblingUpdatePlan.Decide(
            [new InstallCandidate("com.someone.else", 99)],
            new Dictionary<string, long>());

        Assert.Empty(plan);
    }

    [Fact]
    public void MixedRelease_InstallsOnlyTheOwnerAppsThatAdvanced()
    {
        var plan = SiblingUpdatePlan.Decide(
            [
                new InstallCandidate(Launcher, 10),        // newer -> install
                new InstallCandidate(Storyboarder, 3),     // same -> skip
                new InstallCandidate(FutureHealthApp, 1),  // new -> install
                new InstallCandidate("com.evil.app", 100), // not owner -> skip
            ],
            new Dictionary<string, long>
            {
                [Launcher] = 9,
                [Storyboarder] = 3,
            });

        Assert.Equal(
            [Launcher, FutureHealthApp],
            plan.Select(c => c.Package).OrderBy(p => p).ToArray());
    }
}
