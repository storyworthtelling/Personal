using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// Filing and deleting from the wrist, undo-first (W-5.1.4; A14, WD §9, O5; mock: Filed, with Undo). The act happens at
/// once, with no confirmation: it goes to the phone under the watch's own name for it, carrying the window the wrist is
/// showing, and the Undo ring opens. An undo takes back exactly that act, inside the window, once (B2's book); after the
/// ring empties the act is final. Whatever stands in the way is said as a quiet line, and nothing is claimed that did
/// not happen: an act that never reached the phone left the row where it was, and an undo that never reached it left
/// the act standing.
/// </summary>
public class DashUndoTests
{
    private static readonly TimeSpan Six = TimeSpan.FromSeconds(6);

    // Two names made from counted bytes, so no key-shaped run of digits sits in the source (NoKeysTests).
    private static readonly Guid First = new([.. Enumerable.Range(0, 16).Select(index => (byte)index)]);
    private static readonly Guid Second = new([.. Enumerable.Range(16, 16).Select(index => (byte)index)]);

    private static WristAct Filing() => WristAct.File(LeeKey, "Lee", Six, First);

    private static WristAct Landed()
    {
        var act = Filing();
        Assert.Null(act.Told(null));
        return act;
    }

    [Fact]
    public void AFilingGoesAtOnceWithTheWristsWindow()
    {
        var act = Filing();

        var file = Assert.IsType<DashFile>(act.Message);

        Assert.Equal((act.Id, LeeKey, Six), (file.Id, file.Key, file.Window));
        Assert.Empty(file.Messages);
        Assert.Equal(WristActState.Telling, act.State);
    }

    [Fact]
    public void ADeleteIsADelete()
    {
        var act = WristAct.Delete(GroupKey, "Team snacks", Six, First);

        var delete = Assert.IsType<DashDelete>(act.Message);

        Assert.Equal((act.Id, GroupKey, Six), (delete.Id, delete.Key, delete.Window));
        Assert.Equal(WristActKind.Delete, act.Kind);
    }

    [Fact]
    public void TheNameIsTheWatchsOwn()
    {
        var act = Filing();

        Assert.True(DashIds.IsAct(act.Id), act.Id);
        Assert.Equal("act-" + First.ToString("N"), act.Id);
        Assert.Matches("^act-[0-9a-f]{32}$", act.Id);
    }

    [Fact]
    public void TwoActsAreTwoNames() =>
        Assert.NotEqual(WristAct.File(LeeKey, "Lee", Six, First).Id, WristAct.File(LeeKey, "Lee", Six, Second).Id);

    [Fact]
    public void TheWindowIsAtMostTheLongestTheWireCarries()
    {
        var act = WristAct.File(LeeKey, "Lee", TimeSpan.FromMinutes(5), First);

        Assert.Equal(DashLimits.MaxWindow, act.Window);
        Assert.Equal(DashLimits.MaxWindow, Assert.IsType<DashFile>(act.Message).Window);
    }

    [Theory]
    [InlineData("m0123456789abcdef", "Lee")]
    [InlineData("", "Lee")]
    [InlineData("c0123456789abcdef", "")]
    [InlineData("c0123456789abcdef", "  ")]
    public void AnActNeedsACorrespondenceAndAName(string key, string name) =>
        Assert.Throws<ArgumentException>(() => WristAct.File(key, name, Six, First));

    [Fact]
    public void AnActNeedsAWindow() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WristAct.Delete(LeeKey, "Lee", TimeSpan.Zero, First));

    [Fact]
    public void TheWordsSayWhatWasDone()
    {
        Assert.Equal("Lee filed", Filing().Heading);
        Assert.Equal("Lee deleted", WristAct.Delete(LeeKey, "Lee", Six, First).Heading);
        Assert.Equal("Tap to undo", Filing().Prompt);
    }

    [Fact]
    public void TheRingIsOpenWhileTheActIsOnItsWay()
    {
        var act = Filing();

        Assert.True(act.CanUndo);
        Assert.Null(act.Line);
        Assert.False(act.Finished);
        Assert.True(act.Leaves);
    }

    [Fact]
    public void AnUndoInTheWindowTakesBackThatAct()
    {
        var act = Landed();

        var undo = act.Undo(TimeSpan.FromSeconds(3));

        Assert.Equal(new DashUndo(act.Id), undo);
        Assert.Equal(WristActState.Undoing, act.State);
        Assert.False(act.CanUndo);
        Assert.Null(act.Prompt);

        act.UndoTold(null);

        Assert.Equal(WristActState.TakenBack, act.State);
        Assert.True(act.Finished);
        Assert.False(act.Leaves);
        Assert.Null(act.Line);
    }

    [Fact]
    public void AnUndoIsTakenOnce()
    {
        var act = Landed();

        Assert.NotNull(act.Undo(TimeSpan.FromSeconds(1)));
        Assert.Null(act.Undo(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void AnUndoAtTheEndOfTheWindowIsNothing()
    {
        var act = Landed();

        Assert.Null(act.Undo(Six));
        Assert.Equal(WristActState.Open, act.State);
    }

    [Fact]
    public void TheRingEmptyingMakesItFinal()
    {
        var act = Landed();

        act.Closed();

        Assert.Equal(WristActState.Final, act.State);
        Assert.True(act.Finished);
        Assert.True(act.Leaves);
        Assert.False(act.CanUndo);
        Assert.Null(act.Undo(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AnUndoBeforeThePhoneHasTheActWaitsForIt()
    {
        var act = Filing();

        Assert.Null(act.Undo(TimeSpan.FromSeconds(1)));
        Assert.Equal(WristActState.Undoing, act.State);

        // The act reaches the phone after all: the undo goes now, after it, so it finds the act there.
        Assert.Equal(new DashUndo(act.Id), act.Told(null));
        Assert.Equal(WristActState.Undoing, act.State);
    }

    [Fact]
    public void AnActThatNeverReachedThePhoneNeedsNoUndo()
    {
        var act = Filing();
        act.Undo(TimeSpan.FromSeconds(1));

        Assert.Null(act.Told(DashTrouble.NoPhone));
        Assert.Equal(WristActState.TakenBack, act.State);
        Assert.False(act.Leaves);
    }

    [Fact]
    public void AnActThatNeverReachedThePhoneSaysWhy()
    {
        var act = Filing();

        Assert.Null(act.Told(DashTrouble.NoPhone));

        Assert.Equal(WristActState.Failed, act.State);
        Assert.Equal("Your phone isn’t in reach.", act.Line);
        Assert.Equal("Lee", act.Heading);
        Assert.False(act.CanUndo);
        Assert.False(act.Finished);
        Assert.False(act.Leaves);
    }

    [Fact]
    public void AnUndoThatNeverReachedThePhoneLeavesTheActStanding()
    {
        var act = Landed();
        act.Undo(TimeSpan.FromSeconds(2));

        act.UndoTold(DashTrouble.NoPhone);

        Assert.Equal(WristActState.UndoLost, act.State);
        Assert.Equal("The undo didn’t reach your phone.", act.Line);
        Assert.Equal("Lee filed", act.Heading);
        Assert.True(act.Leaves);
        Assert.False(act.Finished);
    }

    [Fact]
    public void TheRingEmptyingBeforeThePhoneHasItWaitsForTheAnswer()
    {
        var act = Filing();

        act.Closed();

        Assert.Equal(WristActState.Telling, act.State);
        Assert.False(act.CanUndo);
        Assert.False(act.Finished);
        Assert.Null(act.Told(null));
        Assert.Equal(WristActState.Final, act.State);
    }

    [Fact]
    public void TheRingEmptyingThenNoPhoneSaysWhy()
    {
        var act = Filing();
        act.Closed();

        act.Told(DashTrouble.NoPhone);

        Assert.Equal(WristActState.Failed, act.State);
        Assert.False(act.Leaves);
    }

    [Fact]
    public void AnActIsToldOnce()
    {
        var act = Landed();

        Assert.Null(act.Told(DashTrouble.NoPhone));
        Assert.Equal(WristActState.Open, act.State);
    }

    [Fact]
    public void TheUndoNeverGoesBeforeTheAct()
    {
        // Only Told hands out an undo for an act still on its way; Undo itself hands one out only once the phone has it.
        var act = Filing();

        Assert.Null(act.Undo(TimeSpan.Zero));
        act.UndoTold(null);

        Assert.Equal(WristActState.Undoing, act.State);
    }
}
