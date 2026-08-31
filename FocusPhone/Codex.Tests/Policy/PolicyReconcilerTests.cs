using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

public class PolicyReconcilerTests
{
    [Fact]
    public void PackagesToRelease_ReturnsEveryInstalledAppNotSuspended()
    {
        // Visibility IS runnability: everything the plan doesn't suspend must be actively un-suspended,
        // so an app a prior pass (or the install watcher) quarantined is freed.
        var installed = new[] { "com.gemini", "com.messages", "com.chrome", "com.game" };
        var suspended = new HashSet<string> { "com.chrome", "com.game" };

        var release = PolicyReconciler.PackagesToRelease(installed, suspended);

        Assert.Equal(new HashSet<string> { "com.gemini", "com.messages" }, release);
    }

    [Fact]
    public void PackagesToRelease_ExcludesEverythingSuspended()
    {
        var installed = new[] { "com.a", "com.b" };
        var suspended = new HashSet<string> { "com.a", "com.b" };

        Assert.Empty(PolicyReconciler.PackagesToRelease(installed, suspended));
    }

    [Fact]
    public void PackagesToRelease_EmptyInstalled_IsEmpty()
    {
        Assert.Empty(PolicyReconciler.PackagesToRelease([], new HashSet<string> { "com.x" }));
    }

    [Fact]
    public void PackagesToRelease_DedupesInstalledInput()
    {
        var release = PolicyReconciler.PackagesToRelease(
            ["com.a", "com.a", "com.b"], new HashSet<string>());

        Assert.Equal(new HashSet<string> { "com.a", "com.b" }, release);
    }
}
