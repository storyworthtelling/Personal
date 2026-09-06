using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// What the corner shows, and why it can only ever mean one thing.
///
/// <para>DESIGN.md §13 gives the fold two jobs that must never overlap: as a <b>notice</b> it marks
/// something waiting, with no clock running; as an <b>undo</b> it carries the back-turn arrow and
/// holds only for its window. An undo therefore takes the corner outright and the notices come back
/// when it closes.</para>
///
/// <para>That rule lived inside the native view — tangled with the host layout, a weak table of
/// pending notices and a Handler — so nothing tested it, and the MAUI kit would have had to
/// reimplement it by reading the same 300 lines. Here it is the decision on its own.</para>
/// </summary>
public class DogEarStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 7, 30, 0, TimeSpan.Zero);

    private static DogEarNotice Notice(string headline = "Mail didn't send") =>
        new(headline, "Gmail refused the message", DogEarHue.Problem);

    [Fact]
    public void AnEmptyCornerShowsNothing()
    {
        Assert.Null(new DogEarState().Fold(Now));
    }

    [Fact]
    public void ANoticeFoldCarriesNoClockAndNoArrow()
    {
        var state = new DogEarState();
        state.SetNotice("mail", Notice());

        var fold = state.Fold(Now);

        Assert.NotNull(fold);
        Assert.Equal("Mail didn't send", fold!.Headline);
        Assert.False(fold.ShowsBackArrow);
        Assert.Null(fold.Expires);
    }

    /// <summary>
    /// Several parts of a screen raise notices independently — a shell watching one thing while a
    /// list watches another — so notices are keyed. A plain replace would mean whichever spoke last
    /// silently erased the other, and the erased one had no way to know.
    /// </summary>
    [Fact]
    public void OneOwnerWithdrawingItsNoticeLeavesEveryoneElsesAlone()
    {
        var state = new DogEarState();
        state.SetNotice("mail", Notice("Mail didn't send"));
        state.SetNotice("sync", Notice("Calendar is out of date"));

        state.SetNotice("mail", null);

        Assert.Equal("Calendar is out of date", state.Fold(Now)!.Headline);
    }

    /// <summary>
    /// One fold, no count (§13): the corner says something waits, not how much. Newest first, so the
    /// thing that just happened is the thing named.
    /// </summary>
    [Fact]
    public void TheCornerNamesTheNewestNoticeAndNeverCountsThem()
    {
        var state = new DogEarState();
        state.SetNotice("mail", Notice("Mail didn't send"));
        state.SetNotice("sync", Notice("Calendar is out of date"));

        var fold = state.Fold(Now)!;

        Assert.Equal("Calendar is out of date", fold.Headline);
        Assert.DoesNotContain("2", fold.Headline);
        Assert.Equal(2, state.Notices.Count);
    }

    [Fact]
    public void AnUndoTakesTheCornerFromAWaitingNotice()
    {
        var state = new DogEarState();
        state.SetNotice("mail", Notice());

        state.BeginUndo("Deleted 2 photos", () => { }, Now, TimeSpan.FromSeconds(8));

        var fold = state.Fold(Now)!;
        Assert.Equal("Deleted 2 photos", fold.Headline);
        Assert.True(fold.ShowsBackArrow);
        Assert.Equal(Now + TimeSpan.FromSeconds(8), fold.Expires);
    }

    /// <summary>The load-bearing half: the notice is not lost, only outranked.</summary>
    [Fact]
    public void TheNoticeComesBackWhenTheUndoWindowCloses()
    {
        var state = new DogEarState();
        state.SetNotice("mail", Notice());
        state.BeginUndo("Deleted 2 photos", () => { }, Now, TimeSpan.FromSeconds(8));

        var after = state.Fold(Now + TimeSpan.FromSeconds(9))!;

        Assert.Equal("Mail didn't send", after.Headline);
        Assert.False(after.ShowsBackArrow);
    }

    [Fact]
    public void TurningTheFoldReversesTheActAndClearsTheWindow()
    {
        var state = new DogEarState();
        var turned = false;
        state.BeginUndo("Deleted 2 photos", () => turned = true, Now, TimeSpan.FromSeconds(8));

        Assert.True(state.Turn(Now + TimeSpan.FromSeconds(2)));

        Assert.True(turned);
        Assert.Null(state.Fold(Now + TimeSpan.FromSeconds(2)));
    }

    /// <summary>
    /// An undo that has already expired cannot be turned. The fold is gone from the screen by then,
    /// but a queued tap can still arrive — and reversing an act the owner watched become permanent
    /// is worse than doing nothing.
    /// </summary>
    [Fact]
    public void AnExpiredUndoCannotBeTurned()
    {
        var state = new DogEarState();
        var turned = false;
        state.BeginUndo("Deleted 2 photos", () => turned = true, Now, TimeSpan.FromSeconds(8));

        Assert.False(state.Turn(Now + TimeSpan.FromSeconds(9)));
        Assert.False(turned);
    }

    /// <summary>Turning a notice fold is not an undo — there is nothing to reverse.</summary>
    [Fact]
    public void TurningANoticeFoldReversesNothing()
    {
        var state = new DogEarState();
        state.SetNotice("mail", Notice());

        Assert.False(state.Turn(Now));
        Assert.Equal("Mail didn't send", state.Fold(Now)!.Headline);
    }

    /// <summary>A new act replaces a fold still showing: one at a time.</summary>
    [Fact]
    public void ASecondUndoReplacesTheFirstAndTheFirstIsNotReversed()
    {
        var state = new DogEarState();
        var first = false;
        state.BeginUndo("Deleted 2 photos", () => first = true, Now, TimeSpan.FromSeconds(8));
        state.BeginUndo("Filed 1 message", () => { }, Now + TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));

        Assert.Equal("Filed 1 message", state.Fold(Now + TimeSpan.FromSeconds(1))!.Headline);
        Assert.False(first);
    }

    [Fact]
    public void ClearingNoticesForgetsWhatWasWaiting()
    {
        var state = new DogEarState();
        state.SetNotice("mail", Notice());

        state.ClearNotices();

        Assert.Null(state.Fold(Now));
        Assert.Empty(state.Notices);
    }
}
