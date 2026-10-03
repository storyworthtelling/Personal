using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// R-G27, the half that needs no clock: a notice that is set again, unchanged, stays where it was.
///
/// <para><c>SetNotice</c> used to insert at index 0 on every call, so the position of a notice meant
/// "the one touched last". Two screens that each re-state their own notices on every render (Ledger's
/// Accounts page, Budget) therefore reshuffled the Waiting page whenever only some of the keys were
/// re-set, and showed the reverse of build order the rest of the time.</para>
/// </summary>
public class DogEarOrderStabilityTests
{
    private static DogEarNotice Notice(string headline) =>
        new(headline, "detail", DogEarHue.Caution);

    [Fact]
    public void ReSettingOneUnchangedNoticeDoesNotMoveItToTheFront()
    {
        var state = new DogEarState();
        state.SetNotice("a", Notice("A"));
        state.SetNotice("b", Notice("B"));
        state.SetNotice("c", Notice("C"));
        var before = state.Notices.Select(n => n.Headline).ToList();

        state.SetNotice("a", Notice("A"));

        Assert.Equal(before, state.Notices.Select(n => n.Headline).ToList());
    }

    [Fact]
    public void ReSettingEveryNoticeAgainAndAgainLeavesTheOrderAlone()
    {
        var state = new DogEarState();
        foreach (var key in new[] { "a", "b", "c" })
        {
            state.SetNotice(key, Notice(key.ToUpperInvariant()));
        }

        var before = state.Notices.Select(n => n.Headline).ToList();

        for (var render = 0; render < 3; render++)
        {
            state.SetNotice("b", Notice("B"));
            state.SetNotice("a", Notice("A"));
            state.SetNotice("c", Notice("C"));
        }

        Assert.Equal(before, state.Notices.Select(n => n.Headline).ToList());
    }
}
