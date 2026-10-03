using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// R-G27: the Waiting page runs newest-first BY TIMESTAMP, undated notices keep a stable insertion
/// order, and neither moves just because its owner stated it again on the next render.
///
/// <para>Order is "the notice with the later date first". Dated notices come before undated ones, since
/// only they can be placed on a clock; among notices on the same clock (equal dates, or none), the one
/// first set most recently goes first — which is the order the corner always had, so a screen with no
/// timestamps behaves exactly as before. "Unchanged" means the words, role, action label and date are
/// the same: the action delegate is a fresh lambda on every render and is deliberately not compared.</para>
/// </summary>
public class DogEarNoticeOrderTests
{
    private static DateTimeOffset Noon(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.Zero);

    private static DogEarNotice Dated(string headline, int month, int day) =>
        new(headline, "detail", DogEarHue.Caution, At: Noon(month, day));

    private static DogEarNotice Undated(string headline) => new(headline, "detail", DogEarHue.Caution);

    private static List<string> Order(DogEarState state) => state.Notices.Select(n => n.Headline).ToList();

    [Fact]
    public void DatedNoticesRunNewestFirstWhateverOrderTheyWereSetIn()
    {
        var state = new DogEarState();
        // The order AccountNotices.Build produces: lost connection, stale balance, Trial.
        state.SetNotice("lost", Dated("Meridian Card needs signing in", 9, 23));
        state.SetNotice("stale", Dated("Evergreen Home Lending is a month old", 9, 26));
        state.SetNotice("trial", Dated("9 of 10 connections used", 9, 14));

        Assert.Equal(
            ["Evergreen Home Lending is a month old", "Meridian Card needs signing in", "9 of 10 connections used"],
            Order(state));
    }

    [Fact]
    public void TheCornerNamesTheNewestByDate()
    {
        var state = new DogEarState();
        state.SetNotice("old", Dated("Older", 9, 14));
        state.SetNotice("new", Dated("Newer", 9, 26));
        state.SetNotice("mid", Dated("Middle", 9, 23));

        Assert.Equal("Newer", state.Fold(Noon(10, 1))!.Headline);
    }

    [Fact]
    public void NoticesOnTheSameDateKeepTheOrderTheyArrivedIn()
    {
        var state = new DogEarState();
        state.SetNotice("x", Dated("X", 9, 20));
        state.SetNotice("y", Dated("Y", 9, 20));
        state.SetNotice("z", Dated("Z", 9, 20));
        var before = Order(state);

        // The owner of "y" states it again, unchanged — the tie must not be re-broken.
        state.SetNotice("y", Dated("Y", 9, 20));
        state.SetNotice("x", Dated("X", 9, 20));

        Assert.Equal(before, Order(state));
    }

    [Fact]
    public void UndatedNoticesKeepAStableInsertionOrder()
    {
        var state = new DogEarState();
        state.SetNotice("a", Undated("A"));
        state.SetNotice("b", Undated("B"));
        state.SetNotice("c", Undated("C"));

        Assert.Equal(["C", "B", "A"], Order(state)); // the order the corner has always had

        state.SetNotice("b", Undated("B"));
        state.SetNotice("a", Undated("A"));

        Assert.Equal(["C", "B", "A"], Order(state));
    }

    [Fact]
    public void ReSettingAnUnchangedDatedNoticeDoesNotMoveIt()
    {
        var state = new DogEarState();
        state.SetNotice("a", Dated("A", 9, 14));
        state.SetNotice("b", Dated("B", 9, 26));
        var before = Order(state);

        for (var render = 0; render < 3; render++)
        {
            state.SetNotice("a", Dated("A", 9, 14));
            state.SetNotice("b", Dated("B", 9, 26));
        }

        Assert.Equal(before, Order(state));
    }

    [Fact]
    public void ADatedNoticeComesBeforeAnUndatedOne()
    {
        var state = new DogEarState();
        state.SetNotice("dated", Dated("Dated", 9, 14));
        state.SetNotice("undated", Undated("Undated"));

        Assert.Equal(["Dated", "Undated"], Order(state));
    }

    [Fact]
    public void ANoticeWhoseWordsChangeIsNewAgainAndMovesToTheFrontAmongItsEquals()
    {
        var state = new DogEarState();
        state.SetNotice("a", Undated("A"));
        state.SetNotice("b", Undated("B"));

        state.SetNotice("a", Undated("A, again"));

        Assert.Equal(["A, again", "B"], Order(state));
    }

    [Fact]
    public void AnUndoStillOutranksTheNoticesAndTheyComeBack()
    {
        var state = new DogEarState();
        var now = Noon(10, 1);
        state.SetNotice("lost", Dated("Meridian Card needs signing in", 9, 23));
        state.SetNotice("stale", Dated("Evergreen Home Lending is a month old", 9, 26));

        state.BeginUndo("Evergreen Home Lending removed", () => { }, now, TimeSpan.FromSeconds(6));

        Assert.True(state.Fold(now)!.ShowsBackArrow);
        Assert.Equal("Evergreen Home Lending is a month old", state.Fold(now + TimeSpan.FromSeconds(7))!.Headline);
    }

    [Fact]
    public void ANoticeWithoutADateCarriesNone()
    {
        Assert.Null(Undated("A").At);
    }

    // ---- the change signal the live Waiting page listens to ----

    [Fact]
    public void NoticesChangedFiresWhenSomethingVisibleChanges_AndNotOnAnIdenticalReSet()
    {
        var state = new DogEarState();
        var fired = 0;
        state.NoticesChanged += () => fired++;

        state.SetNotice("a", Undated("A"));
        Assert.Equal(1, fired);

        state.SetNotice("a", Undated("A")); // a new lambda would be a new delegate; the words are the same
        Assert.Equal(1, fired);

        state.SetNotice("a", Undated("A, changed"));
        Assert.Equal(2, fired);

        state.SetNotice("a", null);
        Assert.Equal(3, fired);

        state.SetNotice("a", null); // withdrawing what is not there says nothing
        Assert.Equal(3, fired);
    }

    [Fact]
    public void ClearingRaisesChangedOnlyWhenSomethingWasWaiting()
    {
        var state = new DogEarState();
        var fired = 0;
        state.NoticesChanged += () => fired++;

        state.ClearNotices();
        Assert.Equal(0, fired);

        state.SetNotice("a", Undated("A"));
        state.ClearNotices();
        Assert.Equal(2, fired);
    }
}
