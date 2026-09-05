using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The empty Mailbox is the ONLY place the owner's voice speaks in Post (§14 permits
/// more; Post deliberately takes less). Everything about which line appears is decided
/// here, so the head only draws and the voice only rewrites.
/// </summary>
public class EmptyMailboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EverySeedCarriesAtLeastOneLine()
    {
        Assert.NotEmpty(EmptyMailbox.Seeds);
        Assert.All(EmptyMailbox.Seeds, seed => Assert.NotEmpty(seed.Rungs));
        Assert.All(EmptyMailbox.Seeds, seed => Assert.All(seed.Rungs, line => Assert.False(string.IsNullOrWhiteSpace(line))));
    }

    [Fact]
    public void SeedIdsAreUnique()
    {
        Assert.Equal(EmptyMailbox.Seeds.Count, EmptyMailbox.Seeds.Select(seed => seed.Id).Distinct().Count());
    }

    // The pool is the point: an empty mailbox that always says the same thing stops
    // being a voice and becomes a label.
    [Fact]
    public void SuccessiveDrawsReachMoreThanOneSeed()
    {
        var state = EmptyMailboxState.Fresh;
        var seen = new HashSet<string>();
        for (var roll = 0; roll < EmptyMailbox.Seeds.Count; roll++)
        {
            var draw = EmptyMailbox.Draw(state, mailArrivedSinceLastView: false, Now, roll);
            seen.Add(draw.Line);
            state = draw.Next;
        }

        Assert.True(seen.Count > 1, "The pool always drew the same line.");
    }

    // A plain seed has one rung and stays on it however often it is drawn — only a
    // seed that carries a ladder escalates.
    [Fact]
    public void APlainSeedNeverEscalates()
    {
        var plain = EmptyMailbox.Seeds.First(seed => seed.Rungs.Count == 1);
        var state = EmptyMailboxState.Fresh;

        for (var i = 0; i < 5; i++)
        {
            var draw = EmptyMailbox.Draw(state, false, Now, RollFor(plain.Id, state, Now));
            Assert.Equal(plain.Rungs[0], draw.Line);
            state = draw.Next;
        }
    }

    // The owner's mechanic: each drawing of a laddered seed steps up a rung, so the
    // line needles harder the more often an unchanged empty mailbox is opened.
    [Fact]
    public void ALadderedSeedStepsUpARungEachDrawing()
    {
        var ladder = EmptyMailbox.Seeds.First(seed => seed.Rungs.Count > 1);
        var state = EmptyMailboxState.Fresh;

        foreach (var expected in ladder.Rungs)
        {
            var draw = EmptyMailbox.Draw(state, false, Now, RollFor(ladder.Id, state, Now));
            Assert.Equal(expected, draw.Line);
            state = draw.Next;
        }
    }

    // Past the last rung the seed goes away for about a month rather than repeating
    // its sharpest line forever, and comes back reset to the top.
    [Fact]
    public void PastTheLastRungTheSeedRetiresAndComesBackReset()
    {
        var ladder = EmptyMailbox.Seeds.First(seed => seed.Rungs.Count > 1);
        var state = ExhaustTheLadder(ladder);

        Assert.DoesNotContain(ladder.Id, EmptyMailbox.Available(state, Now).Select(seed => seed.Id));

        var later = Now.AddDays(EmptyMailbox.RetirementDays + 1);
        Assert.Contains(ladder.Id, EmptyMailbox.Available(state, later).Select(seed => seed.Id));
        Assert.Equal(ladder.Rungs[0], EmptyMailbox.Draw(state, false, later, RollFor(ladder.Id, state, later)).Line);
    }

    // What the ladder counts is views of an UNCHANGED empty mailbox — the compulsive
    // checking, not the absence of mail. Dealing with something and coming back to an
    // empty desk is an achievement, so it starts the count over.
    [Fact]
    public void MailArrivingResetsTheLadder()
    {
        var ladder = EmptyMailbox.Seeds.First(seed => seed.Rungs.Count > 1);
        var state = EmptyMailboxState.Fresh;

        state = EmptyMailbox.Draw(state, false, Now, RollFor(ladder.Id, state, Now)).Next;
        state = EmptyMailbox.Draw(state, false, Now, RollFor(ladder.Id, state, Now)).Next;

        var afterMail = EmptyMailbox.Draw(state, mailArrivedSinceLastView: true, Now, RollFor(ladder.Id, state, Now));
        Assert.Equal(ladder.Rungs[0], afterMail.Line);
    }

    // Mail arriving is not a pardon for a seed that just wore out: it restarts the
    // count, it does not bring a retired line back early.
    [Fact]
    public void MailArrivingDoesNotEndARetirementEarly()
    {
        var ladder = EmptyMailbox.Seeds.First(seed => seed.Rungs.Count > 1);
        var state = ExhaustTheLadder(ladder);

        var available = EmptyMailbox.Draw(state, mailArrivedSinceLastView: true, Now, 0).Next;

        Assert.DoesNotContain(ladder.Id, EmptyMailbox.Available(available, Now).Select(seed => seed.Id));
    }

    // A roll is whatever the caller happens to hand in; the pool must not fall over
    // on a negative one or index past its end.
    [Theory]
    [InlineData(-7)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void AnyRollDrawsARealLine(int roll)
    {
        var draw = EmptyMailbox.Draw(EmptyMailboxState.Fresh, false, Now, roll);

        Assert.False(string.IsNullOrWhiteSpace(draw.Line));
    }

    // Finds the roll that lands on a given seed, so a test can drive one seed's
    // behaviour without depending on where it sits in the pool.
    private static int RollFor(string seedId, EmptyMailboxState state, DateTimeOffset now)
    {
        var available = EmptyMailbox.Available(state, now);
        var index = available.ToList().FindIndex(seed => seed.Id == seedId);
        Assert.True(index >= 0, $"Seed '{seedId}' is not currently available.");
        return index;
    }

    private static EmptyMailboxState ExhaustTheLadder(EmptySeed ladder)
    {
        var state = EmptyMailboxState.Fresh;
        foreach (var _ in ladder.Rungs)
        {
            state = EmptyMailbox.Draw(state, false, Now, RollFor(ladder.Id, state, Now)).Next;
        }
        return state;
    }
}
