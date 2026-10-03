using Index.Domain.Connect;
using Xunit;

namespace Index.Tests.Connect;

/// <summary>
/// R-G7 (R3): the connect page's gate is "is Chrome suspended", not "which app is the default
/// browser". The default browser stays Flyleaf's restricted viewer even while Admin mode is open, so
/// the old test could not tell the two states apart and the page said "Admin mode needed" forever.
/// These pin the pure decision; the PackageManager read that feeds it is Android and is covered by
/// <c>ConnectPageSourceTests</c> at the source level.
/// </summary>
public class AdminModeStateTests
{
    [Fact]
    public void ChromeInstalledAndSuspended_AdminModeIsNeeded()
    {
        Assert.Equal(AdminModeState.Needed,
            ConnectBrowserPolicy.DecideAdminMode(chromeInstalled: true, chromeSuspended: true));
    }

    [Fact]
    public void ChromeInstalledAndRunnable_AdminModeIsOpen()
    {
        Assert.Equal(AdminModeState.Open,
            ConnectBrowserPolicy.DecideAdminMode(chromeInstalled: true, chromeSuspended: false));
    }

    [Fact]
    public void ChromeNotInstalled_IsItsOwnState_NotAdminModeNeeded()
    {
        // Telling someone to open Admin mode so a browser they do not have will run is the wrong
        // sentence; this is a third state with its own words.
        Assert.Equal(AdminModeState.ChromeMissing,
            ConnectBrowserPolicy.DecideAdminMode(chromeInstalled: false, chromeSuspended: false));
    }

    [Fact]
    public void ChromeNotInstalled_WinsOverAStaleSuspendedFlag()
    {
        Assert.Equal(AdminModeState.ChromeMissing,
            ConnectBrowserPolicy.DecideAdminMode(chromeInstalled: false, chromeSuspended: true));
    }

    [Fact]
    public void TheChromePackageIsTheStockOne()
    {
        // The manifest's <queries> entry must name exactly this package or Android 11+ hides it and
        // "not installed" would be reported on a phone that has it.
        Assert.Equal("com.android.chrome", ConnectBrowserPolicy.ChromePackage);
    }

    [Fact]
    public void TheRestrictedViewerIsNoLongerWhatDecidesAdminMode()
    {
        // The viewer check survives for the fallback message only. Whatever the default browser is,
        // the decision depends on Chrome's state alone — there is no overload that takes a package.
        var methods = typeof(ConnectBrowserPolicy).GetMethods().Where(m => m.Name == "DecideAdminMode").ToList();
        var method = Assert.Single(methods);
        Assert.All(method.GetParameters(), p => Assert.Equal(typeof(bool), p.ParameterType));
    }
}
