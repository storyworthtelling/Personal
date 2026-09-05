using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The Darkroom survives being closed, because it holds the only copy of everything in it.
/// A room that emptied on a process death would lose the owner's pictures outright, which
/// is the one failure the reversed spine cannot afford.
/// </summary>
public class DarkroomCodecTests
{
    private static readonly DateTime Noon = new(2026, 9, 4, 12, 34, 56, DateTimeKind.Local);

    [Fact]
    public void ARoomComesBackAsItWent()
    {
        var room = Darkroom.Empty
            .Add(DarkroomItem.Photo("a.jpg", Noon))
            .Add(DarkroomItem.Photo("b.jpg", Noon.AddMinutes(1)))
            .WithCaption("a.jpg", "Four goes at the same tree.")
            .Add(DarkroomItem.Scan("s", Noon.AddMinutes(2), "Insurance claim", 7,
                ScanDestination.Remarkable))
            .WithAttempt("s");

        var back = DarkroomCodec.Read(DarkroomCodec.Write(room));

        Assert.Equal(3, back.Items.Count);
        Assert.Equal(["s", "b.jpg", "a.jpg"], back.Items.Select(item => item.Id));
        Assert.Equal("Four goes at the same tree.", back.Items.Single(i => i.Id == "a.jpg").Caption);

        var scan = back.Items.Single(item => item.Id == "s");
        Assert.Equal(DarkroomKind.Scan, scan.Kind);
        Assert.Equal("Insurance claim", scan.Title);
        Assert.Equal(7, scan.Pages);
        Assert.Equal(ScanDestination.Remarkable, scan.Destination);
        Assert.Equal(1, scan.Attempts);
    }

    [Fact]
    public void CapturedTimesSurviveIntactBecauseTheOrderDependsOnThem()
    {
        var room = Darkroom.Empty.Add(DarkroomItem.Photo("a.jpg", Noon));

        Assert.Equal(Noon, DarkroomCodec.Read(DarkroomCodec.Write(room)).Items[0].CapturedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"items\":\"wrong shape\"}")]
    [InlineData("[]")]
    public void GarbledOrAbsentReadsAsAnEmptyRoomRatherThanThrowing(string? stored)
    {
        // The alternative is a head that cannot launch. An empty room is visibly wrong and
        // recoverable; a crash on start is neither.
        Assert.False(DarkroomCodec.Read(stored).HasAnything);
    }

    [Fact]
    public void AnEmptyRoomRoundTrips()
    {
        // Distinct from garbled: a room the owner has emptied must stay empty rather than
        // being treated as corrupt and re-read from somewhere else.
        Assert.False(DarkroomCodec.Read(DarkroomCodec.Write(Darkroom.Empty)).HasAnything);
    }

    [Fact]
    public void ChoicesAreNotPersisted()
    {
        // A selection is a gesture in progress, not state. Coming back to a room with three
        // things silently ticked from yesterday is how someone deletes the wrong pictures.
        var room = Darkroom.Empty.Add(DarkroomItem.Photo("a.jpg", Noon)).Choose("a.jpg");

        Assert.Equal(0, DarkroomCodec.Read(DarkroomCodec.Write(room)).ChosenCount);
    }
}
