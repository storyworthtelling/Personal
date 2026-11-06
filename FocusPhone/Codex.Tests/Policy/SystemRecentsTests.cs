using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

/// <summary>
/// On a Pixel the stock launcher also runs the swipe-up gesture and recents. Suspending it with the
/// other home apps made every swipe up (and sometimes boot) open the "blocked by your admin" dialog
/// instead of returning to Flyleaf, so the recents provider is kept runnable.
/// </summary>
public class SystemRecentsTests
{
    private const string PixelLauncher = "com.google.android.apps.nexuslauncher";

    private static PolicyContext Context(string? recents, IEnumerable<string>? otherHomes = null) =>
        new(
            OwnPackages: PolicyContext.FocusPhonePackages,
            BrowserPackages: new HashSet<string> { "com.android.chrome" },
            OtherHomePackages: (otherHomes ?? [PixelLauncher, "com.teslacoilsw.launcher"]).ToHashSet(),
            DebuggingLockedDown: false,
            NeverVisiblePackages: new HashSet<string>(),
            AdminModeActive: false,
            SystemRecentsPackage: recents);

    [Fact]
    public void TheRecentsProvider_IsNotSuspended_ThoughItIsAnotherHome()
    {
        var plan = PolicyPlanBuilder.Build(Context(PixelLauncher));

        Assert.DoesNotContain(PixelLauncher, plan.SuspendedPackages);
    }

    [Fact]
    public void TheRecentsProvider_IsActivelyReleased_SinceAnEarlierPassSuspendedIt()
    {
        var plan = PolicyPlanBuilder.Build(Context(PixelLauncher));

        Assert.Contains(PixelLauncher, plan.RunnablePackages);
    }

    [Fact]
    public void OtherHomeApps_StaySuspended()
    {
        var plan = PolicyPlanBuilder.Build(Context(PixelLauncher));

        Assert.Contains("com.teslacoilsw.launcher", plan.SuspendedPackages);
    }

    [Fact]
    public void WithNoRecentsProviderKnown_EveryOtherHomeIsSuspended()
    {
        var plan = PolicyPlanBuilder.Build(Context(recents: null));

        Assert.Contains(PixelLauncher, plan.SuspendedPackages);
        Assert.DoesNotContain(PixelLauncher, plan.RunnablePackages);
    }

    [Fact]
    public void TheRecentsProvider_IsNeverReleasedWhenItIsANeverVisibleApp()
    {
        // A misconfigured device naming a game as its recents component must not free the game.
        var context = Context("com.king.candycrushsaga") with
        {
            NeverVisiblePackages = new HashSet<string> { "com.king.candycrushsaga" },
        };

        var plan = PolicyPlanBuilder.Build(context);

        Assert.Contains("com.king.candycrushsaga", plan.SuspendedPackages);
    }

    [Theory]
    [InlineData("com.google.android.apps.nexuslauncher/com.android.quickstep.RecentsActivity", PixelLauncher)]
    [InlineData("com.android.launcher3/com.android.quickstep.RecentsActivity", "com.android.launcher3")]
    [InlineData("com.android.systemui/.recents.RecentsActivity", "com.android.systemui")]
    public void ThePackageIsReadFromTheFlattenedComponent(string component, string expected) =>
        Assert.Equal(expected, SystemRecents.PackageFrom(component));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/com.android.quickstep.RecentsActivity")]
    public void AnEmptyOrMalformedComponentNamesNoPackage(string? component) =>
        Assert.Null(SystemRecents.PackageFrom(component));

    [Fact]
    public void APackageWithoutAClassIsStillAPackage() =>
        Assert.Equal(PixelLauncher, SystemRecents.PackageFrom(PixelLauncher));
}
