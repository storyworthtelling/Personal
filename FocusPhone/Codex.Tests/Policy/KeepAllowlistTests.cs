using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

/// <summary>Google Keep is cleared by the lockdown the way GitHub, Gemini, Maps and reMarkable are (C-10.1.4).</summary>
public class KeepAllowlistTests
{
    private static PolicyContext Context(bool adminModeActive, IEnumerable<string>? browsers = null) =>
        new(
            OwnPackages: PolicyContext.FocusPhonePackages,
            BrowserPackages: (browsers ?? ["com.android.chrome"]).ToHashSet(),
            OtherHomePackages: new HashSet<string>(),
            DebuggingLockedDown: false,
            NeverVisiblePackages: new HashSet<string>(),
            AdminModeActive: adminModeActive);

    [Fact]
    public void KeepIsTheKnownPackage() =>
        Assert.Equal("com.google.android.keep", PolicyContext.KeepPackage);

    [Fact]
    public void KeepIsAllowlisted() =>
        Assert.Contains(PolicyContext.KeepPackage, PolicyContext.AllowlistedPackages);

    [Fact]
    public void KeepIsAllowedWhateverItsRating()
    {
        var keep = new AppInfo(PolicyContext.KeepPackage, HandlesHttpViewIntents: false, IsCategoryGame: false);

        Assert.Equal(AppVerdict.Allowed, new AppCategorizer().Categorize(keep, ContentRating.Unknown));
    }

    [Fact]
    public void KeepIsAllowedEvenIfItHandlesLinks()
    {
        var keep = new AppInfo(PolicyContext.KeepPackage, HandlesHttpViewIntents: true, IsCategoryGame: false);

        Assert.Equal(AppVerdict.Allowed, new AppCategorizer().Categorize(keep, ContentRating.Unknown));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepIsUnsuspendedInEveryMode(bool admin)
    {
        var plan = PolicyPlanBuilder.Build(Context(admin, [PolicyContext.KeepPackage]));

        Assert.Contains(PolicyContext.KeepPackage, plan.RunnablePackages);
        Assert.DoesNotContain(PolicyContext.KeepPackage, plan.SuspendedPackages);
    }

    [Fact]
    public void KeepIsInNoBlockedSet()
    {
        Assert.DoesNotContain(PolicyContext.KeepPackage, PolicyContext.LoginBrowserPackages);
        Assert.DoesNotContain(PolicyContext.KeepPackage, PolicyContext.FocusPhonePackages);
    }
}
