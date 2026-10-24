using Constitution.Domain.Ink;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// The "Still sore?" tick is printed under one id on every page (review finding N2), so a blank on one page and a question
/// still open on another arrive under the same id. A note is only cleared when every page said better; a page that is waiting
/// on an answer has not said it.
/// </summary>
public class SorenessSharedIdTests
{
    private static readonly string[] Shared = ["sore"];

    private static SorenessLedger Noted() => SorenessLedger.Empty.Note(Sam, "left knee", Day(1));

    private static SorenessLedger Settled(params InkResolution[] resolutions) =>
        Noted().Settle(Sam, Day(2), SessionOutcome.Done, Shared, resolutions);

    private static bool Open(SorenessLedger ledger) => ledger.PageModel(Sam, Day(3)) is not null;

    [Fact]
    public void EveryPageSayingBetterClearsIt() =>
        Assert.False(Open(Settled(new InkResolution.Better("sore"), new InkResolution.Better("sore"), new InkResolution.Better("sore"))));

    [Fact]
    public void ABetterBesideAnOpenQuestionKeepsTheNote() =>
        Assert.True(Open(Settled(
            new InkResolution.Better("sore"),
            new InkResolution.NeedsAsking("sore", "left knee", "Still sore", "Better", "read:left knee:Still sore|Better"))));

    [Fact]
    public void ABetterBesideAPageNotReadKeepsTheNote() =>
        Assert.True(Open(Settled(new InkResolution.Better("sore"), new InkResolution.NotRead("sore", "page not read"))));

    [Fact]
    public void ABetterBesideAnUndecidedBoxKeepsTheNote() =>
        Assert.True(Open(Settled(new InkResolution.Better("sore"), new InkResolution.Undecided("sore"))));

    [Fact]
    public void ABetterBesideAStillSoreKeepsTheNote() =>
        Assert.True(Open(Settled(new InkResolution.Better("sore"), new InkResolution.StillSore("sore"))));

    [Fact]
    public void TheQuestionComingFirstChangesNothing() =>
        Assert.True(Open(Settled(new InkResolution.NotRead("sore", "page not read"), new InkResolution.Better("sore"))));

    [Fact]
    public void AnswersToOtherBoxesAreNotThisOnesBusiness() =>
        Assert.False(Open(Settled(new InkResolution.Better("sore"), new InkResolution.NotRead("done", "page not read"))));
}
