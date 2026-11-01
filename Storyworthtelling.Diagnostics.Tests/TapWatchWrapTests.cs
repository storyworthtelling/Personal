using Xunit;

namespace Storyworthtelling.Diagnostics.Tests;

// The pure part of the tap watch, native and MAUI alike (plan §4.1): what PaperUi's buttons wrap their onClick in, and
// the key-action handle a background act reports through. Codex.Ui.TapWatch is the Android glue over it.
public class TapWatchWrapTests
{
    private long now;
    private readonly List<TapVerdict> reported = [];

    private TapWatchCore Core()
    {
        var core = new TapWatchCore(() => now, reported.Add);
        core.ScreenShown("Settings");
        return core;
    }

    [Fact]
    public void AHandlerThatThrowsIsReportedAndStillThrows()
    {
        var core = Core();
        core.Touched("Save", new TapPoint(10, 10), enabled: true, isControl: true);
        var wrapped = core.Wrap("Save", () => throw new InvalidOperationException("handler fault"));

        var thrown = Assert.Throws<InvalidOperationException>(wrapped);

        Assert.Equal("handler fault", thrown.Message);
        var verdict = Assert.Single(reported);
        Assert.Equal(TapVerdictKind.Threw, verdict.Kind);
        Assert.Equal("Settings", verdict.Screen);
        Assert.Equal("Save", verdict.Control);
        Assert.Contains("handler fault", verdict.Detail);
    }

    [Fact]
    public void AWrappedHandlerRunsAndAQuietOneIsJudgedDeadOnTheNextTick()
    {
        var core = Core();
        var ran = false;
        core.Touched("Save", new TapPoint(10, 10), enabled: true, isControl: true);
        core.Wrap("Save", () => ran = true)();

        now = 1_500;
        core.Tick();

        Assert.True(ran);
        Assert.Equal(TapVerdictKind.Dead, Assert.Single(reported).Kind);
    }

    [Fact]
    public void TheDeadWindowCountsFromWhenTheWrappedHandlerReturned()
    {
        var core = Core();
        core.Touched("Save", new TapPoint(10, 10), enabled: true, isControl: true);
        core.Wrap("Save", () => now = 2_000)(); // a two-second handler

        now = 2_100;
        core.ContentChanged();
        now = 10_000;
        core.Tick();

        Assert.Empty(reported);
    }

    [Fact]
    public void AKeyActionStartedFromAHandlerIsNeverDead()
    {
        var core = Core();
        core.Touched("Sign in with Google", new TapPoint(10, 10), enabled: true, isControl: true);
        KeyAction? action = null;
        core.Wrap("Sign in with Google", () => action = core.Action("post.gmail-sign-in", TimeSpan.FromMinutes(5)))();

        now = 20_000;
        action!.Succeeded();
        core.Tick();

        Assert.Empty(reported);
    }

    [Fact]
    public void AFailedKeyActionIsReportedWithItsReason()
    {
        var core = Core();
        var action = core.Action("post.send-sms", TimeSpan.FromSeconds(30));

        now = 3_000;
        action.Failed("RESULT_ERROR_NO_SERVICE");

        var verdict = Assert.Single(reported);
        Assert.Equal(TapVerdictKind.ActionFailed, verdict.Kind);
        Assert.Equal("post.send-sms", verdict.Control);
        Assert.Equal("RESULT_ERROR_NO_SERVICE", verdict.Detail);
    }

    [Fact]
    public void OnlyTheFirstReportOfAKeyActionCounts()
    {
        var core = Core();
        var action = core.Action("post.send-sms", TimeSpan.FromSeconds(30));

        action.Succeeded();
        action.Failed("late failure after success");

        Assert.Empty(reported);
    }

    [Fact]
    public void AKeyActionThatNeverReportsIsNoResult()
    {
        var core = Core();
        core.Action("post.send-mms", TimeSpan.FromSeconds(60));

        now = 60_000;
        core.Tick();

        Assert.Equal(TapVerdictKind.ActionNoResult, Assert.Single(reported).Kind);
    }

    [Fact]
    public void AKeyActionMayReportFromAnotherThread()
    {
        var core = Core();
        var actions = Enumerable.Range(0, 200).Select(i => core.Action($"post.send-{i}", TimeSpan.FromSeconds(30))).ToList();

        Parallel.ForEach(actions, a => a.Failed("radio off"));

        Assert.Equal(200, reported.Count(v => v.Kind == TapVerdictKind.ActionFailed));
    }

    [Fact]
    public void AReportThatThrowsNeverBreaksTheTap()
    {
        var core = new TapWatchCore(() => now, _ => throw new IOException("log full"));
        core.ScreenShown("Settings");
        core.Touched("Save", new TapPoint(10, 10), enabled: true, isControl: true);
        core.Wrap("Save", () => { })();

        now = 5_000;
        var thrown = Record.Exception(core.Tick);

        Assert.Null(thrown);
    }

    [Fact]
    public void TheNextDeadlineSaysWhenTheHookShouldTickAgain()
    {
        var core = Core();
        Assert.Null(core.NextTickDelayMs());

        core.Touched("Save", new TapPoint(10, 10), enabled: true, isControl: true);

        Assert.Equal(TapWatchCore.TickAfterTapMs, core.NextTickDelayMs());
    }

    [Theory]
    [InlineData("Save", null, "Button", "Save")]
    [InlineData("  Send logs now  ", "send_logs", "Button", "Send logs now #send_logs")]
    [InlineData("", "row_title", "TextView", "#row_title")]
    [InlineData(null, null, "LinearLayout", "LinearLayout")]
    public void AControlIsNamedTheSameWayByTheWrapAndByTheTouchHook(string? text, string? id, string type, string expected)
    {
        Assert.Equal(expected, TapWatchCore.ControlName(text, id, type));
    }

    [Fact]
    public void ALongLabelIsCutSoAMessageBodyDoesNotBecomeAControlName()
    {
        var name = TapWatchCore.ControlName(new string('w', 200), null, "TextView");

        Assert.Equal(TapWatchCore.MaxLabelLength + 1, name.Length);
        Assert.EndsWith("…", name);
    }
}
