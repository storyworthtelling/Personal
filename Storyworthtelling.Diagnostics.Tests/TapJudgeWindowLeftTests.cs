using Xunit;

namespace Storyworthtelling.Diagnostics.Tests;

// A tap whose answer opens in another window (another app's screen, a platform dialog, the system's own picker)
// changes nothing in the window the watch fingerprints: the window loses focus instead. And a long press opens its
// answer while the finger is still down, before the tap is fed at lift. Neither is a dead tap.
public class TapJudgeWindowLeftTests
{
    private static readonly TapPoint Spot = new(100, 200);

    [Fact]
    public void ATapThatTakesTheWindowAwayIsNotDead()
    {
        var judge = new TapJudge();
        judge.Tap(0, "Settings", "Sign in", Spot, enabled: true, isControl: true);

        judge.WindowLeft(200);

        Assert.Empty(judge.Tick(3_000));
    }

    [Fact]
    public void AChangeWhileTheFingerWasDownAnswersTheTap()
    {
        var judge = new TapJudge();
        judge.ContentChanged(450, "HomeActivity");

        judge.Tap(500, "HomeActivity", "Book (long press)", Spot, enabled: true, isControl: true, pressedAtMs: 0);

        Assert.Empty(judge.Tick(3_000));
    }

    [Fact]
    public void LeavingTheWindowWhileTheFingerWasDownAnswersTheTap()
    {
        var judge = new TapJudge();
        judge.WindowLeft(450);

        judge.Tap(500, "HomeActivity", "Book (long press)", Spot, enabled: true, isControl: true, pressedAtMs: 0);

        Assert.Empty(judge.Tick(3_000));
    }

    [Fact]
    public void AChangeBeforeThePressDoesNotAnswerIt()
    {
        var judge = new TapJudge();
        judge.ContentChanged(100, "HomeActivity");

        judge.Tap(500, "HomeActivity", "Book (long press)", Spot, enabled: true, isControl: true, pressedAtMs: 200);

        Assert.Equal(TapVerdictKind.Dead, Assert.Single(judge.Tick(3_000)).Kind);
    }

    [Fact]
    public void WithNoChangeAtAllThePressedTapIsStillDead()
    {
        var judge = new TapJudge();

        judge.Tap(500, "HomeActivity", "Book (long press)", Spot, enabled: true, isControl: true, pressedAtMs: 0);

        Assert.Equal(TapVerdictKind.Dead, Assert.Single(judge.Tick(3_000)).Kind);
    }

    [Fact]
    public void TheCoreClearsATapWhenItsWindowIsLeft()
    {
        long now = 0;
        var reported = new List<TapVerdict>();
        var core = new TapWatchCore(() => now, reported.Add);
        core.ScreenShown("HomeActivity");
        core.Touched("Uninstall", Spot, enabled: true, isControl: true);

        now = 200;
        core.WindowLeft();
        now = 3_000;
        core.Tick();

        Assert.Empty(reported);
    }

    [Fact]
    public void TheCorePassesThePressTimeToTheJudge()
    {
        long now = 0;
        var reported = new List<TapVerdict>();
        var core = new TapWatchCore(() => now, reported.Add);
        core.ScreenShown("HomeActivity");

        now = 450;
        core.ContentChanged();
        now = 500;
        core.Touched("Book (long press)", Spot, enabled: true, isControl: true, textEntry: false, pressedAtMs: 0);
        now = 3_000;
        core.Tick();

        Assert.Empty(reported);
    }
}
