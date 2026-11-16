using Folio.Domain.Analysis;
using Folio.Domain.Reading;
using Folio.Domain.Text;
using Xunit;

namespace Folio.Tests.Reading;

// The round table (release H, R28–R33, R40): the rules that decide whose turn it is, what is owed and when
// the show closes. The model chooses what is said; these choose who may say it.
public class RoundTablePlanTests
{
    private static Review Read(string persona, int score) =>
        new(persona, "v", "a", ["s"], ["w"], "c") { Score = score, ScoreReason = "r" };

    // Hugo Hook (agent) at 8 and Nick Picker (critic) at 3 are five apart; everyone else is close.
    private static readonly IReadOnlyList<Review> Panel =
    [
        Read("developmental-editor", 6), Read("agent", 8), Read("acquiring-editor", 6),
        Read("genre-fan", 7), Read("harsh-critic", 3), Read("literature-professor", 6), Read("fellow-writer", 6),
    ];

    [Theory]
    [InlineData(StoryLength.ShortStory, 15)]
    [InlineData(StoryLength.Novella, 30)]
    [InlineData(StoryLength.Novel, 50)]
    public void TheTurnCapScalesWithTheManuscript(StoryLength length, int cap) =>
        Assert.Equal(cap, RoundTablePlan.TurnCap(length));

    [Fact]
    public void ASplitIsTwoScoredReadersThreeOrMoreApart()
    {
        var splits = RoundTablePlan.Splits(Panel);

        Assert.Equal(new HashSet<RoundTableSplit>
        {
            new RoundTableSplit("agent", "harsh-critic"),
            new RoundTableSplit("genre-fan", "harsh-critic"),
            new RoundTableSplit("developmental-editor", "harsh-critic"),
            new RoundTableSplit("acquiring-editor", "harsh-critic"),
            new RoundTableSplit("harsh-critic", "literature-professor"),
            new RoundTableSplit("harsh-critic", "fellow-writer"),
        }, splits.ToHashSet());
        Assert.Empty(RoundTablePlan.Splits([Read("agent", 8), Read("harsh-critic", 0)]));
    }

    [Fact]
    public void TheHostOpensTheShow()
    {
        var step = RoundTablePlan.Next(RoundTable.Empty, Panel, StoryLength.Novel);

        Assert.Equal(RoundTableStepKind.HostTurn, step.Kind);
    }

    [Fact]
    public void TheHostsMoveIsSpokenAndCuesTheReaderItNamed()
    {
        var table = RoundTablePlan.Apply(RoundTable.Empty,
            new HostMove("Welcome. Hugo, you loved it.", "agent", "What sold you?", "", Close: false), Panel);

        Assert.Equal(new RoundTableTurn(RoundTablePlan.HostId, "Welcome. Hugo, you loved it."), Assert.Single(table.Turns));
        Assert.Equal(new RoundTableCue("agent", "What sold you?"), table.Next);
        var step = RoundTablePlan.Next(table, Panel, StoryLength.Novel);
        Assert.Equal(RoundTableStepKind.ReaderTurn, step.Kind);
        Assert.Equal("agent", step.Cue!.Speaker);
    }

    [Fact]
    public void AReaderSpeaksToTheCueAndRecordsWhomItAnswered()
    {
        var table = RoundTablePlan.Apply(RoundTable.Empty,
            new HostMove("Nick?", "harsh-critic", "Answer Hugo.", "agent", Close: false), Panel);

        table = RoundTablePlan.Spoken(table, "Hugo is wrong about the opening.");

        var turn = table.Turns[^1];
        Assert.Equal("harsh-critic", turn.Speaker);
        Assert.Equal("agent", turn.Answering);
        Assert.Null(table.Next);
        Assert.Equal(RoundTableStepKind.HostTurn, RoundTablePlan.Next(table, Panel, StoryLength.Novel).Kind);
    }

    /// <summary>A speaker the host made up, or the host itself, is not a reader: the quietest reader is cued.</summary>
    [Fact]
    public void ACueToSomeoneNotOnThePanelGoesToTheQuietestReader()
    {
        var table = RoundTablePlan.Apply(RoundTable.Empty, new HostMove("Hello.", "agent", "First?", "", false), Panel);
        table = RoundTablePlan.Spoken(table, "I loved it.");

        table = RoundTablePlan.Apply(table, new HostMove("And?", "Somebody Else", "Go on.", "nobody", false), Panel);

        Assert.NotEqual("agent", table.Next!.Speaker);
        Assert.Contains(Panel, review => review.PersonaId == table.Next.Speaker);
        Assert.Equal(string.Empty, table.Next.Answering);
    }

    /// <summary>The host may close early only once every split has had its rebuttals (R30, R40).</summary>
    [Fact]
    public void TheHostCannotCloseWhileARebuttalIsOwed()
    {
        var table = RoundTablePlan.Apply(RoundTable.Empty, new HostMove("Hello.", "agent", "First?", "", false), Panel);
        table = RoundTablePlan.Spoken(table, "I loved it.");

        table = RoundTablePlan.Apply(table, new HostMove("That's our show.", "", "", "", Close: true), Panel);

        Assert.False(table.CloseAsked);
        Assert.NotNull(table.Next);
        Assert.NotEqual(string.Empty, table.Next!.Answering);
    }

    [Fact]
    public void OnceEveryRebuttalIsGivenTheHostMayCloseAndTheShowEnds()
    {
        var panel = new[] { Read("agent", 8), Read("harsh-critic", 4), Read("literature-professor", 6) };
        var table = RoundTable.Empty;
        foreach (var (speaker, answering) in new[] { ("agent", "harsh-critic"), ("harsh-critic", "agent") })
        {
            table = RoundTablePlan.Apply(table, new HostMove("Over to you.", speaker, "Answer.", answering, false), panel);
            table = RoundTablePlan.Spoken(table, "My answer.");
        }
        Assert.Empty(RoundTablePlan.Owed(table, panel));

        table = RoundTablePlan.Apply(table, new HostMove("Thanks, all.", "", "", "", Close: true), panel);

        Assert.True(table.CloseAsked);
        Assert.Equal(RoundTableStepKind.Close, RoundTablePlan.Next(table, panel, StoryLength.Novel).Kind);
        table = RoundTablePlan.Closed(table, "Goodnight.", new RoundTableSummary(["The prose"], ["The ending"]));
        Assert.Equal(RoundTableStepKind.Done, RoundTablePlan.Next(table, panel, StoryLength.Novel).Kind);
        Assert.Equal("Goodnight.", table.Turns[^1].Words);
    }

    /// <summary>
    /// The cap is a cap, not a quota, but R30's rebuttals are owed whatever the length: past the cap they
    /// are cued straight, with no host turn between, and then the show closes.
    /// </summary>
    [Fact]
    public void PastTheCapOwedRebuttalsAreCuedStraightThenTheShowCloses()
    {
        var panel = new[] { Read("agent", 8), Read("harsh-critic", 3) };
        var table = RoundTable.Empty;
        for (var i = 0; i < RoundTablePlan.TurnCap(StoryLength.ShortStory); i++)
            table = table with { Turns = [.. table.Turns, new RoundTableTurn(RoundTablePlan.HostId, "Chatter.")] };

        var step = RoundTablePlan.Next(table, panel, StoryLength.ShortStory);
        Assert.Equal(RoundTableStepKind.ReaderTurn, step.Kind);
        table = RoundTablePlan.Spoken(table with { Next = step.Cue }, "Answer one.");
        step = RoundTablePlan.Next(table, panel, StoryLength.ShortStory);
        Assert.Equal(RoundTableStepKind.ReaderTurn, step.Kind);
        table = RoundTablePlan.Spoken(table with { Next = step.Cue }, "Answer two.");

        Assert.Equal(RoundTableStepKind.Close, RoundTablePlan.Next(table, panel, StoryLength.ShortStory).Kind);
    }

    [Fact]
    public void SpeakersAreNamedByTheirReaderNamesAndTheHostAsTaeBoll()
    {
        Assert.Equal("Tae Boll", RoundTablePlan.Name(RoundTablePlan.HostId));
        Assert.Equal("Hugo Hook", RoundTablePlan.Name("agent"));
    }

    [Fact]
    public void TheTableRoundTripsAndDamageIsAnEmptyTable()
    {
        var table = RoundTablePlan.Apply(RoundTable.Empty, new HostMove("Hello.", "agent", "First?", "", false), Panel);
        table = RoundTablePlan.Spoken(table, "I loved it.");
        table = RoundTablePlan.Closed(table, "Bye.", new RoundTableSummary(["a"], ["b"]));

        var read = RoundTableCodec.Read(RoundTableCodec.Write(table));

        Assert.Equal(table.Turns, read.Turns);
        Assert.Equal(["a"], read.Summary!.Agreed);
        Assert.Equal(["b"], read.Summary.StillDiffer);
        Assert.Empty(RoundTableCodec.Read("garbage").Turns);
        Assert.Empty(RoundTableCodec.Read(null).Turns);
    }
}
