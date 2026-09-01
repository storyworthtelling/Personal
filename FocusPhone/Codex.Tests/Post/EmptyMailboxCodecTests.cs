using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The pool's state is only worth anything if it survives the process: the ladder counts
/// visits across launches, and a state that reset every time the app was killed would
/// never reach its second rung.
/// </summary>
public class EmptyMailboxCodecTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AFreshStateRoundTrips()
    {
        var back = EmptyMailboxCodec.Decode(EmptyMailboxCodec.Encode(EmptyMailboxState.Fresh));

        Assert.Empty(back.Rungs);
        Assert.Empty(back.RetiredUntilUtc);
    }

    [Fact]
    public void RungsAndRetirementsRoundTrip()
    {
        var state = new EmptyMailboxState(
            new Dictionary<string, int> { ["go-away"] = 2, ["waiting"] = 0 },
            new Dictionary<string, DateTimeOffset> { ["go-away"] = Now.AddDays(30) });

        var back = EmptyMailboxCodec.Decode(EmptyMailboxCodec.Encode(state));

        Assert.Equal(2, back.Rungs["go-away"]);
        Assert.Equal(0, back.Rungs["waiting"]);
        Assert.Equal(Now.AddDays(30), back.RetiredUntilUtc["go-away"]);
    }

    // A climbed ladder has to still be climbed after a restart, which is the whole point.
    [Fact]
    public void ADecodedStateKeepsClimbingWhereItLeftOff()
    {
        var ladder = EmptyMailbox.Seeds.First(seed => seed.Rungs.Count > 1);
        var state = EmptyMailbox.Draw(EmptyMailboxState.Fresh, false, Now, RollFor(ladder.Id, EmptyMailboxState.Fresh, Now)).Next;

        var restarted = EmptyMailboxCodec.Decode(EmptyMailboxCodec.Encode(state));

        Assert.Equal(ladder.Rungs[1], EmptyMailbox.Draw(restarted, false, Now, RollFor(ladder.Id, restarted, Now)).Line);
    }

    // Nothing stored yet, and anything unreadable, both mean "start over" rather than
    // "crash the empty state" — the pool is decoration, not data worth failing for.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"Rungs\":")]
    [InlineData("[]")]
    public void GarbledOrAbsentDecodesToFresh(string stored)
    {
        var back = EmptyMailboxCodec.Decode(stored);

        Assert.Empty(back.Rungs);
        Assert.Empty(back.RetiredUntilUtc);
    }

    private static int RollFor(string seedId, EmptyMailboxState state, DateTimeOffset now)
    {
        var available = EmptyMailbox.Available(state, now).ToList();
        return available.FindIndex(seed => seed.Id == seedId);
    }
}
