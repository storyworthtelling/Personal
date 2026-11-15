using Xunit;

namespace Storyworthtelling.Diagnostics.Tests;

// A tap inside a web page (a form field, a script-driven button) changes content the watch's layout fingerprint
// cannot see, so judging it dead is a guess the watch is in no position to make. It still counts for repeats:
// pressing one spot again and again is worth reporting whatever the page did.
public class TapWatchUnseenTests
{
    private long now;
    private readonly List<TapVerdict> reported = [];

    private TapWatchCore Core()
    {
        var core = new TapWatchCore(() => now, reported.Add);
        core.ScreenShown("BrowserActivity");
        return core;
    }

    [Fact]
    public void ATapWhoseResultTheWatchCannotSeeIsNeverJudgedDead()
    {
        var core = Core();

        core.TouchedUnseen("WebView", new TapPoint(120, 300), enabled: true);
        now = 5_000;
        core.Tick();

        Assert.Empty(reported);
    }

    [Fact]
    public void ItStillCountsForRepeats()
    {
        var core = Core();

        core.TouchedUnseen("WebView", new TapPoint(120, 300), enabled: true);
        now = 600;
        core.TouchedUnseen("WebView", new TapPoint(122, 301), enabled: true);
        now = 1_200;
        core.TouchedUnseen("WebView", new TapPoint(121, 299), enabled: true);
        now = 10_000;
        core.Tick();

        var verdict = Assert.Single(reported);
        Assert.Equal(TapVerdictKind.Repeated, verdict.Kind);
        Assert.Equal(3, verdict.Count);
        Assert.Equal("BrowserActivity", verdict.Screen);
    }

    [Fact]
    public void AnOrdinaryControlBesideItIsStillJudged()
    {
        var core = Core();

        core.TouchedUnseen("WebView", new TapPoint(120, 300), enabled: true);
        now = 100;
        core.Touched("Back", new TapPoint(20, 40), enabled: true, isControl: true);
        now = 5_000;
        core.Tick();

        var verdict = Assert.Single(reported);
        Assert.Equal(TapVerdictKind.Dead, verdict.Kind);
        Assert.Equal("Back", verdict.Control);
    }
}
