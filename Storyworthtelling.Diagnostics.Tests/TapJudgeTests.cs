namespace Storyworthtelling.Diagnostics.Tests;

// The pure judge behind the tap watch (plan §4.1, O8, O10). Times are milliseconds on one monotonic clock;
// every call first settles whatever has matured by its own time, so the judge never needs a timer.
public class TapJudgeTests
{
    private static readonly TapPoint Spot = new(100, 200);

    private static TapJudge Judge() => new();

    [Fact]
    public void ATapThatChangesNothingWithinTheWindowIsDead()
    {
        var judge = Judge();
        judge.Tap(0, "Settings", "Save", Spot, enabled: true, isControl: true);

        Assert.Empty(judge.Tick(1_499));
        var verdicts = judge.Tick(1_500);

        var dead = Assert.Single(verdicts);
        Assert.Equal(TapVerdictKind.Dead, dead.Kind);
        Assert.Equal("Settings", dead.Screen);
        Assert.Equal("Save", dead.Control);
        Assert.Equal("DEAD TAP", dead.Label);
    }

    [Theory]
    [InlineData("screen")]
    [InlineData("content")]
    [InlineData("status")]
    public void AnyVisibleChangeInsideTheWindowMeansTheTapWorked(string change)
    {
        var judge = Judge();
        judge.Tap(0, "Settings", "Save", Spot, enabled: true, isControl: true);

        _ = change switch
        {
            "screen" => judge.ScreenChanged(400, "Diagnostics"),
            "content" => judge.ContentChanged(400, "Settings"),
            _ => judge.StatusChanged(400, "Settings"),
        };

        Assert.Empty(judge.Tick(5_000));
    }

    [Fact]
    public void AChangeAfterTheWindowComesTooLate()
    {
        var judge = Judge();
        judge.Tap(0, "Settings", "Save", Spot, enabled: true, isControl: true);

        var verdicts = judge.ContentChanged(1_600, "Settings");

        Assert.Equal(TapVerdictKind.Dead, Assert.Single(verdicts).Kind);
    }

    [Fact]
    public void ADeclaredActionIsNeverJudgedDead()
    {
        var judge = Judge();
        judge.Tap(0, "Mailbox", "Send", Spot, enabled: true, isControl: true);
        judge.ActionStarted(20, "post.send", TimeSpan.FromSeconds(30));
        judge.ActionSucceeded(9_000, "post.send");

        Assert.Empty(judge.Tick(60_000));
    }

    [Fact]
    public void ATapWhileADeclaredActionIsRunningIsNotDead()
    {
        var judge = Judge();
        judge.ActionStarted(0, "post.sync", TimeSpan.FromSeconds(30));
        judge.Tap(1_000, "Mailbox", "Row", Spot, enabled: true, isControl: true);

        Assert.DoesNotContain(judge.Tick(5_000), v => v.Kind == TapVerdictKind.Dead);
    }

    [Fact]
    public void ThreeTapsOnOneControlInsideFourSecondsAreRepeated()
    {
        var judge = Judge();
        foreach (var at in new long[] { 0, 1_000, 2_000 })
        {
            judge.Tap(at, "Mailbox", "Send", Spot, enabled: true, isControl: true);
            judge.ContentChanged(at + 100, "Mailbox"); // each press did something
        }

        var verdicts = judge.Tick(10_000);

        var repeated = Assert.Single(verdicts);
        Assert.Equal(TapVerdictKind.Repeated, repeated.Kind);
        Assert.Equal(3, repeated.Count);
        Assert.Equal("REPEATED TAP ×3", repeated.Label);
    }

    [Fact]
    public void TapsNearOneSpotCountAsTheSameTargetEvenUnderDifferentNames()
    {
        var judge = Judge();
        judge.Tap(0, "Mailbox", "row 1", new TapPoint(100, 200), enabled: true, isControl: true);
        judge.ContentChanged(50, "Mailbox");
        judge.Tap(500, "Mailbox", "row 2", new TapPoint(130, 220), enabled: true, isControl: true);
        judge.ContentChanged(550, "Mailbox");
        judge.Tap(1_000, "Mailbox", "row 3", new TapPoint(110, 240), enabled: true, isControl: true);
        judge.ContentChanged(1_050, "Mailbox");

        var repeated = Assert.Single(judge.Tick(10_000));
        Assert.Equal(TapVerdictKind.Repeated, repeated.Kind);
        Assert.Equal(3, repeated.Count);
    }

    [Fact]
    public void TwoTapsAreNotRepeatedAndThreeSpreadOverMoreThanFourSecondsAreNot()
    {
        var judge = Judge();
        foreach (var at in new long[] { 0, 3_000, 6_000 })
        {
            judge.Tap(at, "Mailbox", "Send", Spot, enabled: true, isControl: true);
            judge.ContentChanged(at + 100, "Mailbox");
        }

        Assert.Empty(judge.Tick(20_000));
    }

    [Fact]
    public void ABurstIsReportedOnceWithItsFinalCount()
    {
        var judge = Judge();
        var verdicts = new List<TapVerdict>();
        for (var i = 0; i < 6; i++)
        {
            verdicts.AddRange(judge.Tap(i * 300, "Mailbox", "Send", Spot, enabled: true, isControl: true));
            verdicts.AddRange(judge.ContentChanged(i * 300 + 50, "Mailbox"));
        }
        verdicts.AddRange(judge.Tick(30_000));

        var repeated = Assert.Single(verdicts);
        Assert.Equal(6, repeated.Count);
    }

    [Fact]
    public void MashingADeadButtonReportsOneDeadTapAndOneRepeat()
    {
        var judge = Judge();
        var verdicts = new List<TapVerdict>();
        verdicts.AddRange(judge.Tap(0, "Settings", "Save", Spot, enabled: true, isControl: true));
        verdicts.AddRange(judge.Tap(400, "Settings", "Save", Spot, enabled: true, isControl: true));
        verdicts.AddRange(judge.Tap(800, "Settings", "Save", Spot, enabled: true, isControl: true));
        verdicts.AddRange(judge.Tick(30_000));

        Assert.Single(verdicts, v => v.Kind == TapVerdictKind.Dead);
        Assert.Single(verdicts, v => v.Kind == TapVerdictKind.Repeated && v.Count == 3);
        Assert.Equal(2, verdicts.Count);
    }

    [Fact]
    public void ADisabledControlIsLoggedOnlyWhenRepeated()
    {
        var once = Judge();
        once.Tap(0, "Settings", "Save", Spot, enabled: false, isControl: true);
        once.Tap(500, "Settings", "Save", Spot, enabled: false, isControl: true);
        Assert.Empty(once.Tick(30_000));

        var thrice = Judge();
        thrice.Tap(0, "Settings", "Save", Spot, enabled: false, isControl: true);
        thrice.Tap(500, "Settings", "Save", Spot, enabled: false, isControl: true);
        thrice.Tap(1_000, "Settings", "Save", Spot, enabled: false, isControl: true);
        var verdict = Assert.Single(thrice.Tick(30_000));
        Assert.Equal(TapVerdictKind.RepeatedOnDisabled, verdict.Kind);
        Assert.Equal("REPEATED TAP ×3 ON DISABLED", verdict.Label);
    }

    [Fact]
    public void RepeatedTapsOnSomethingThatIsNotAControlAreReportedAsSuch()
    {
        var once = Judge();
        once.Tap(0, "Mailbox", "TextView", Spot, enabled: true, isControl: false);
        Assert.Empty(once.Tick(30_000));

        var judge = Judge();
        judge.Tap(0, "Mailbox", "TextView", Spot, enabled: true, isControl: false);
        judge.Tap(600, "Mailbox", "TextView", Spot, enabled: true, isControl: false);
        judge.Tap(1_200, "Mailbox", "TextView", Spot, enabled: true, isControl: false);

        var verdict = Assert.Single(judge.Tick(30_000));
        Assert.Equal(TapVerdictKind.NonControl, verdict.Kind);
        Assert.Equal("TAPS ON NON-CONTROL", verdict.Label);
    }

    [Fact]
    public void AHandlerThatThrowsIsReportedAtOnceAndIsNotAlsoDead()
    {
        var judge = Judge();
        judge.Tap(0, "Settings", "Save", Spot, enabled: true, isControl: true);

        var thrown = judge.Threw(5, "Settings", "Save", "System.InvalidOperationException: boom");

        var verdict = Assert.Single(thrown);
        Assert.Equal(TapVerdictKind.Threw, verdict.Kind);
        Assert.Equal("TAP THREW", verdict.Label);
        Assert.Contains("InvalidOperationException", verdict.Detail);
        Assert.Empty(judge.Tick(30_000));
    }

    [Fact]
    public void AFailedActionIsReportedWithItsReason()
    {
        var judge = Judge();
        judge.ActionStarted(0, "post.send", TimeSpan.FromSeconds(30));

        var verdict = Assert.Single(judge.ActionFailed(2_000, "post.send", "carrier refused"));

        Assert.Equal(TapVerdictKind.ActionFailed, verdict.Kind);
        Assert.Equal("ACTION FAILED", verdict.Label);
        Assert.Equal("post.send", verdict.Control);
        Assert.Equal("carrier refused", verdict.Detail);
    }

    [Fact]
    public void AnActionThatNeverReportsWithinItsWindowIsNoResult()
    {
        var judge = Judge();
        judge.ActionStarted(0, "post.send", TimeSpan.FromSeconds(30));

        Assert.Empty(judge.Tick(29_999));
        var verdict = Assert.Single(judge.Tick(30_000));

        Assert.Equal(TapVerdictKind.ActionNoResult, verdict.Kind);
        Assert.Equal("ACTION NO RESULT", verdict.Label);
        Assert.Empty(judge.ActionSucceeded(31_000, "post.send"));
    }

    [Fact]
    public void TheRingKeepsTheLastTwentyTapsNewestLast()
    {
        var judge = Judge();
        for (var i = 1; i <= 25; i++)
        {
            judge.Tap(i * 10_000, "Mailbox", $"control {i}", new TapPoint(i * 100, 0), enabled: true, isControl: true);
            judge.ContentChanged(i * 10_000 + 10, "Mailbox");
        }

        Assert.Equal(TapJudge.RingSize, judge.Recent.Count);
        Assert.Equal(20, TapJudge.RingSize);
        Assert.Equal("control 6", judge.Recent[0].Control);
        Assert.Equal("control 25", judge.Recent[^1].Control);
    }

    [Fact]
    public void AProblemLineCarriesTheRingAsContext()
    {
        var judge = Judge();
        judge.Tap(0, "Mailbox", "Filed", new TapPoint(0, 0), enabled: true, isControl: true);
        judge.ScreenChanged(100, "Filed");
        judge.Tap(2_000, "Filed", "Save", Spot, enabled: true, isControl: true);

        var dead = Assert.Single(judge.Tick(4_000));
        var text = dead.ToLogText();

        Assert.Equal(2, dead.Recent.Count);
        Assert.StartsWith("DEAD TAP — screen Filed · control Save", text);
        Assert.Contains("Mailbox · Filed · 3500 ms before", text);
        Assert.Contains("Filed · Save · 1500 ms before", text);
    }

    [Fact]
    public void TheDefaultsAreThePlansStartingPoints()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1_500), TapJudge.DeadWindow);
        Assert.Equal(TimeSpan.FromSeconds(4), TapJudge.RepeatWindow);
        Assert.Equal(3, TapJudge.RepeatCount);
        Assert.Equal(48f, TapJudge.RepeatRadius);
    }
}
