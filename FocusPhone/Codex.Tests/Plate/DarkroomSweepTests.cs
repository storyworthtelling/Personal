using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

// Error-handling §18: the Darkroom holds the only copy of each photo, and the row naming a capture was saved
// after the picture was written, asynchronously. A crash in between left the picture in Plate's private folder
// where nothing showed it and nothing ever looked. Startup now sweeps the folder and puts any picture the room
// does not list back into it, dated by the file.
public class DarkroomSweepTests
{
    private static readonly DateTime Shot = new(2026, 11, 8, 9, 30, 0);

    [Fact]
    public void APictureTheRoomDoesNotListIsPutBack()
    {
        var room = Darkroom.Empty.Add(DarkroomItem.Photo("20261108_093000_001.jpg", Shot));

        var swept = DarkroomSweep.Recover(room, [new SweptFile("20261108_093000_001.jpg", Shot), new SweptFile("20261108_093500_002.jpg", Shot.AddMinutes(5))]);

        Assert.Equal(2, swept.Items.Count);
        var recovered = swept.Items.Single(item => item.Id == "20261108_093500_002.jpg");
        Assert.Equal(DarkroomKind.Photo, recovered.Kind);
        Assert.Equal(Shot.AddMinutes(5), recovered.CapturedAt);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("partial.jpg.tmp")]
    [InlineData("")]
    public void AnythingThatIsNotAPictureIsLeftAlone(string name)
    {
        Assert.Same(Darkroom.Empty, DarkroomSweep.Recover(Darkroom.Empty, [new SweptFile(name, Shot)]));
    }

    [Fact]
    public void ARoomThatListsEverythingIsReturnedUnchanged()
    {
        var room = Darkroom.Empty.Add(DarkroomItem.Photo("a.jpg", Shot));

        Assert.Same(room, DarkroomSweep.Recover(room, [new SweptFile("a.jpg", Shot)]));
    }
}
