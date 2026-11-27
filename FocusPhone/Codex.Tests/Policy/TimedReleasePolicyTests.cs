using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

/// <summary>
/// The Entertainment block against a timed release: a streaming service the lockdown hard-suspends is left
/// runnable exactly while its release window is open, and suspended again the moment it closes.
/// </summary>
public class TimedReleasePolicyTests
{
    private const string Netflix = "com.netflix.mediaclient";

    private static PolicyContext Context(IEnumerable<string>? released = null) =>
        new(
            OwnPackages: PolicyContext.FocusPhonePackages,
            BrowserPackages: new HashSet<string>(),
            OtherHomePackages: new HashSet<string>(),
            DebuggingLockedDown: false,
            NeverVisiblePackages: new HashSet<string> { Netflix },
            AdminModeActive: false,
            TimedReleasedPackages: released is null ? null : new HashSet<string>(released));

    [Fact]
    public void AStreamingServiceIsSuspendedWithNoRelease()
    {
        Assert.Contains(Netflix, PolicyPlanBuilder.Build(Context()).SuspendedPackages);
    }

    [Fact]
    public void AStreamingServiceIsRunnableWhileItsWindowIsOpen()
    {
        var plan = PolicyPlanBuilder.Build(Context(released: [Netflix]));

        Assert.DoesNotContain(Netflix, plan.SuspendedPackages);
    }

    [Fact]
    public void AReleaseLiftsOnlyItsOwnService()
    {
        var context = Context(released: [Netflix]) with
        {
            NeverVisiblePackages = new HashSet<string> { Netflix, "com.hulu.plus" },
        };

        Assert.Contains("com.hulu.plus", PolicyPlanBuilder.Build(context).SuspendedPackages);
    }
}
