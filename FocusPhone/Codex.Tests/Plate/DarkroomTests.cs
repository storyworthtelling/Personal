using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The room everything waits in.
///
/// <para>This is the phase's reversed spine. Gate 1 settled on "Plate never keeps anything";
/// the owner reversed it: <b>nothing leaves Plate until you have chosen it, and nothing
/// stays once you have.</b> Culling is a real job nobody does inside Google Photos, and
/// someone who shoots a lot and never goes back is exactly who needs it.</para>
///
/// <para>What that costs was accepted knowingly. Under the old rule a photo was safe the
/// instant the shutter closed; now Plate holds the <b>only</b> copy until the owner acts.
/// Four mitigations were offered — auto-send after a week, auto-send what was never opened,
/// a fold that refuses to flatten, or nothing — and <b>nothing</b> was chosen: "my darkroom,
/// my job." So there is no auto-send, no expiry and no nag anywhere in these tests, and
/// their absence is the design rather than an omission.</para>
/// </summary>
public class DarkroomTests
{
    private static readonly DateTime Noon = new(2026, 9, 4, 12, 34, 56, DateTimeKind.Local);

    private static DarkroomItem Photo(string id, int minute = 0) =>
        DarkroomItem.Photo(id, Noon.AddMinutes(minute));

    [Fact]
    public void AnEmptyRoomHasNothingToActOn()
    {
        var room = Darkroom.Empty;

        Assert.Empty(room.Items);
        Assert.False(room.HasAnything);
        Assert.Equal(0, room.ChosenCount);
    }

    [Fact]
    public void TheNewestIsFirst()
    {
        // A grid you scroll to reach what you just shot is a grid you stop using. The last
        // thing captured is the thing being decided about.
        var room = Darkroom.Empty.Add(Photo("a", 0)).Add(Photo("b", 5)).Add(Photo("c", 2));

        Assert.Equal(["b", "c", "a"], room.Items.Select(item => item.Id));
    }

    [Fact]
    public void TheSameCaptureCannotLandTwice()
    {
        // A rebind or a rotation can replay the save callback; a doubled row would show the
        // owner two of a photo they took once and then ask them to cull it.
        var room = Darkroom.Empty.Add(Photo("a")).Add(Photo("a"));

        Assert.Single(room.Items);
    }

    [Fact]
    public void ChoosingCountsWhatWasPicked()
    {
        var room = Darkroom.Empty.Add(Photo("a")).Add(Photo("b")).Add(Photo("c"))
            .Choose("a").Choose("c");

        Assert.Equal(2, room.ChosenCount);
        Assert.True(room.IsChosen("a"));
        Assert.False(room.IsChosen("b"));
    }

    [Fact]
    public void ChoosingTwiceUnchoosesIt()
    {
        var room = Darkroom.Empty.Add(Photo("a")).Choose("a").Choose("a");

        Assert.Equal(0, room.ChosenCount);
    }

    [Fact]
    public void ChoosingSomethingThatIsGoneIsNotAnError()
    {
        // A tap that lands after a delete has already rebuilt the grid.
        var room = Darkroom.Empty.Add(Photo("a")).Choose("ghost");

        Assert.Equal(0, room.ChosenCount);
    }

    [Fact]
    public void SendingTakesTheChosenOnesOutOfTheRoom()
    {
        // "Only after sent or deleted does it get removed from Darkroom."
        var room = Darkroom.Empty.Add(Photo("a")).Add(Photo("b")).Choose("a");
        var (remaining, sent) = room.TakeChosen();

        Assert.Equal(["a"], sent.Select(item => item.Id));
        Assert.Equal(["b"], remaining.Items.Select(item => item.Id));
        Assert.Equal(0, remaining.ChosenCount);
    }

    [Fact]
    public void SendingNothingChosenSendsEverything()
    {
        // The bar says "Send all" when nothing is picked, so that is what it must do.
        var room = Darkroom.Empty.Add(Photo("a")).Add(Photo("b"));
        var (remaining, sent) = room.TakeChosen();

        Assert.Equal(2, sent.Count);
        Assert.False(remaining.HasAnything);
    }

    [Fact]
    public void ADeleteIsUndoFirstAndKeepsWhatItTook()
    {
        // §7: destructive acts are undo-first, never a confirm dialog. This is a decision
        // made twenty times a day, and a dialog between the owner and each one is the
        // reason nobody culls.
        var room = Darkroom.Empty.Add(Photo("a")).Add(Photo("b")).Choose("a");
        var (after, removed) = room.TakeChosen();

        Assert.Equal(["a"], removed.Select(item => item.Id));
        Assert.Equal(["b"], after.Items.Select(item => item.Id));

        // Putting them back is the undo, and it restores them where they were.
        var undone = after.Restore(removed);
        Assert.Equal(["b", "a"], undone.Items.Select(item => item.Id));
    }

    [Fact]
    public void RestoringSomethingStillPresentDoesNotDuplicateIt()
    {
        var room = Darkroom.Empty.Add(Photo("a"));

        Assert.Single(room.Restore([Photo("a")]).Items);
    }

    [Fact]
    public void ACaptionIsAttachedWithoutDisturbingAnythingElse()
    {
        var room = Darkroom.Empty.Add(Photo("a")).Add(Photo("b"))
            .WithCaption("a", "Four goes at the same tree.");

        Assert.Equal("Four goes at the same tree.", room.Items.Single(i => i.Id == "a").Caption);
        Assert.Equal(string.Empty, room.Items.Single(i => i.Id == "b").Caption);
    }

    [Fact]
    public void CaptioningSomethingThatHasGoneIsNotAnError()
    {
        // The caption arrives from a slow call. By then the owner may have deleted it.
        var room = Darkroom.Empty.Add(Photo("a"));

        Assert.Single(room.WithCaption("ghost", "…").Items);
    }

    [Fact]
    public void AHandedOverScanWaitsInTheSameRoomAsThePictures()
    {
        // Everything that has not left waits here: pictures to choose between, and the scan
        // whose handoff has not been confirmed, in one room rather than two.
        var room = Darkroom.Empty
            .Add(Photo("a"))
            .Add(DarkroomItem.Scan("s", Noon, "Insurance claim", 7, ScanDestination.Remarkable));

        Assert.Equal(2, room.Items.Count);
        Assert.Equal(DarkroomKind.Scan, room.Items.Single(i => i.Id == "s").Kind);
    }

    [Fact]
    public void NothingLeavesOnItsOwn()
    {
        // The load-bearing absence. There is no expiry, no auto-send and no nag: the owner
        // was offered all three and chose none of them. If this type ever grows a clock,
        // this test is the thing that should have to be deleted first.
        var room = Darkroom.Empty.Add(Photo("a"));
        var members = typeof(Darkroom).GetMembers()
            .Select(member => member.Name)
            .Where(name =>
                name.Contains("Expire", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Auto", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Sweep", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(members);
        Assert.True(room.HasAnything);
    }
}
