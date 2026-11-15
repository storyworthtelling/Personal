using Xunit;

namespace Storyworthtelling.Diagnostics.Tests;

// An activity that is resumed rather than recreated (back to the home screen from a browser it opened) never runs its
// create path again, so the watch has to hear that it is showing once more — or every tap there is judged on the
// screen that was created last.
public class TapWatchScreenReturnTests
{
    private long now;
    private readonly List<TapVerdict> reported = [];

    private TapWatchCore Core() => new(() => now, reported.Add);

    [Fact]
    public void TapsAfterReturningToAnEarlierScreenAreJudgedOnIt()
    {
        var core = Core();
        var home = new object();
        var browser = new object();
        core.ScreenShown("HomeActivity", home);
        core.ScreenShown("BrowserActivity", browser);

        core.ScreenReturned(home);
        core.Touched("Open", new TapPoint(10, 10), enabled: true, isControl: true);
        now = 2_000;
        core.Tick();

        Assert.Equal("HomeActivity", core.Screen);
        Assert.Equal("HomeActivity", Assert.Single(reported).Screen);
    }

    [Fact]
    public void AnAreaShownInsideTheScreenIsWhatComesBack()
    {
        var core = Core();
        var shell = new object();
        var reader = new object();
        core.ScreenShown("ShellActivity", shell);
        core.ScreenShown("Inbox");
        core.ScreenShown("ReaderActivity", reader);

        core.ScreenReturned(shell);

        Assert.Equal("Inbox", core.Screen);
    }

    [Fact]
    public void RegainingFocusOnTheScreenAlreadyShowingIsNotAChange()
    {
        var core = Core();
        var home = new object();
        core.ScreenShown("HomeActivity", home);
        core.Touched("Open", new TapPoint(10, 10), enabled: true, isControl: true);

        core.ScreenReturned(home); // a system dialog over the same screen closed
        now = 2_000;
        core.Tick();

        Assert.Equal(TapVerdictKind.Dead, Assert.Single(reported).Kind);
    }

    [Fact]
    public void AnOwnerNeverShownChangesNothing()
    {
        var core = Core();
        core.ScreenShown("HomeActivity", new object());

        core.ScreenReturned(new object());

        Assert.Equal("HomeActivity", core.Screen);
    }
}
