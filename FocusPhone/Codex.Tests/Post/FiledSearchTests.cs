using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Filed is searched, not listed: the owner describes what they want and Claude picks up to
/// five from a compact catalogue of what is filed. These are the pure halves — the catalogue,
/// reading the answer, the word match used without a key, and the session that asks for more.
/// </summary>
public class FiledSearchTests
{
    private static readonly DateTimeOffset Day = new(2026, 3, 4, 15, 0, 0, TimeSpan.Zero);

    private static FiledEntry Entry(
        string id, string who = "robin@example.com", string? subject = "Quote", string? excerpt = "Here it is",
        int daysAgo = 0, TimelineItemKind kind = TimelineItemKind.Email, bool incoming = true) =>
        new(id, Day, Day.AddDays(-daysAgo), kind, incoming, who, subject, excerpt);

    [Fact]
    public void TheCatalogueNumbersEachLineAndKeepsTheFactsSearchedFor()
    {
        var catalogue = FiledCatalogue.Build(
            [Entry("a", subject: "Bathroom quote", excerpt: "Tile and labour"),
             Entry("b", who: "5550101", subject: null, excerpt: "Running late", daysAgo: 1,
                 kind: TimelineItemKind.Message, incoming: false) with { Who = "Robin Hale" }],
            query: "plumber");

        var lines = catalogue.Text.Split('\n');
        Assert.Equal("1 | 2026-03-04 | email from | robin@example.com | Bathroom quote | Tile and labour", lines[0]);
        Assert.Equal("2 | 2026-03-03 | text to | Robin Hale |  | Running late", lines[1]);
        Assert.Equal("a", catalogue.IdAt(1));
        Assert.Equal(2, catalogue.LineOf("b"));
        Assert.Null(catalogue.IdAt(3));
        Assert.Equal(2, catalogue.Sent);
        Assert.Equal(2, catalogue.Total);
    }

    // One line per entry, whatever the message carried: a newline or a pipe inside a subject
    // would otherwise start a fake line or a fake column.
    [Fact]
    public void ALineCannotBreakTheCatalogue()
    {
        var catalogue = FiledCatalogue.Build(
            [Entry("a", subject: "Part one\nPart | two", excerpt: new string('x', 1_000))], query: "x");

        var line = Assert.Single(catalogue.Text.Split('\n'));
        Assert.Contains("Part one Part / two", line);
        Assert.True(line.Length < 400);
    }

    // The newest first, so the model reads recent correspondence before old, and a cut
    // falls on what is oldest.
    [Fact]
    public void TheCatalogueReadsNewestFirst()
    {
        var catalogue = FiledCatalogue.Build([Entry("old", daysAgo: 30), Entry("new", daysAgo: 1)], query: "");

        Assert.Equal("new", catalogue.IdAt(1));
        Assert.Equal("old", catalogue.IdAt(2));
    }

    // An archive too big to send keeps every entry the words match, then the newest of the rest.
    [Fact]
    public void AnArchiveTooBigKeepsWordMatchesFirstThenTheNewest()
    {
        var entries = Enumerable.Range(0, 50)
            .Select(i => Entry($"e{i}", subject: "Newsletter", excerpt: "Weekly", daysAgo: i))
            .Append(Entry("plumb", subject: "Plumber invoice", daysAgo: 400))
            .ToList();

        var catalogue = FiledCatalogue.Build(entries, query: "the plumber", maxChars: 600);

        Assert.True(catalogue.Text.Length <= 600);
        Assert.NotNull(catalogue.LineOf("plumb"));
        Assert.NotNull(catalogue.LineOf("e0"));
        Assert.Null(catalogue.LineOf("e49"));
        Assert.Equal(51, catalogue.Total);
        Assert.True(catalogue.Sent < 51);
    }

    private static FiledCatalogue Three() =>
        FiledCatalogue.Build([Entry("a"), Entry("b", daysAgo: 1), Entry("c", daysAgo: 2)], query: "");

    [Fact]
    public void AnAnswerNamesLinesBestFirstWithHowWellEachFits()
    {
        var answer = FiledAnswer.Parse(
            """{"matches":[{"line":2,"fit":"strong"},{"line":1,"fit":"weak"}]}""", Three(), shown: []);

        Assert.Equal([new FiledPick("b", true), new FiledPick("a", false)], answer.Picks);
        Assert.Equal(0, answer.Rejected);
        Assert.False(answer.Garbled);
    }

    // Each of these is the model doing its job badly, so each is counted for the log rather
    // than silently dropped: a line that is not there, a line already shown, a repeat, and
    // anything past five.
    [Fact]
    public void LinesThatCannotBeUsedAreRejectedAndCounted()
    {
        var catalogue = FiledCatalogue.Build(
            Enumerable.Range(0, 9).Select(i => Entry($"e{i}", daysAgo: i)).ToList(), query: "");

        var answer = FiledAnswer.Parse(
            """{"matches":[{"line":99},{"line":1},{"line":2},{"line":2},{"line":3},{"line":4},{"line":5},{"line":6},{"line":7}]}""",
            catalogue, shown: ["e0"]);

        Assert.Equal(["e1", "e2", "e3", "e4", "e5"], answer.Picks.Select(p => p.SourceId));
        Assert.Equal(4, answer.Rejected); // 99, e0 already shown, the repeat, and the one past five
    }

    [Fact]
    public void AnAnswerWrappedInProseStillReads()
    {
        var answer = FiledAnswer.Parse("""Here you go: {"matches":[{"line":3,"fit":"strong"}]} hope that helps""", Three(), []);

        Assert.Equal("c", Assert.Single(answer.Picks).SourceId);
    }

    [Fact]
    public void NoMatchesIsAnAnswerNotAFailure()
    {
        var answer = FiledAnswer.Parse("""{"matches":[]}""", Three(), []);

        Assert.Empty(answer.Picks);
        Assert.False(answer.Garbled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("I could not find anything.")]
    [InlineData("""{"lines":[1]}""")]
    public void AGarbledAnswerSaysSo(string? reply)
    {
        var answer = FiledAnswer.Parse(reply, Three(), []);

        Assert.Empty(answer.Picks);
        Assert.True(answer.Garbled);
    }

    // Without a key, Post still finds what the words name: the most words matched first,
    // the newest among equals, filler words ignored, accents folded.
    [Fact]
    public void TheWordMatchRanksByWordsThenByDate()
    {
        var entries = new[]
        {
            Entry("one", subject: "Plumber", daysAgo: 1),
            Entry("both", subject: "Plumber's invoice", daysAgo: 5),
            Entry("newer", subject: "Plumber visit", daysAgo: 0),
            Entry("none", subject: "Dinner"),
            Entry("accent", who: "Zoë", subject: "Plumber invoice", daysAgo: 9),
        };

        var found = FiledWords.Find(entries, "the invoice from the plumber", exclude: [], take: 5);

        Assert.Equal(["both", "accent", "newer", "one"], found);
        Assert.Equal(["accent"], FiledWords.Find(entries, "zoe", exclude: [], take: 5));
        Assert.Equal(["newer", "one"], FiledWords.Find(entries, "plumber", exclude: ["both", "accent"], take: 5));
    }

    [Fact]
    public void AQueryOfOnlyFillerWordsFindsNothingByWords()
    {
        Assert.Empty(FiledWords.Find([Entry("a", subject: "the")], "the one about that", exclude: [], take: 5));
    }

    // The cross-check for the log: entries every content word appears in.
    [Fact]
    public void CompleteMatchesAreThoseWithEveryWord()
    {
        var entries = new[] { Entry("both", subject: "Plumber invoice"), Entry("one", subject: "Plumber") };

        Assert.Equal(["both"], FiledWords.Complete(entries, "plumber invoice"));
        Assert.Empty(FiledWords.Complete(entries, "the"));
    }

    [Fact]
    public void ASessionGathersRoundsWithoutRepeatsAndKnowsWhenItIsDone()
    {
        var session = FiledSearchSession.Start("the plumber");
        Assert.Equal(0, session.Round);
        Assert.False(session.Exhausted);

        session = session.Record(["a", "b"]);
        Assert.Equal(1, session.Round);
        Assert.Equal(["a", "b"], session.Shown);
        Assert.False(session.Exhausted);

        session = session.Record(["b", "c"]);
        Assert.Equal(["a", "b", "c"], session.Shown);
        Assert.Equal(2, session.Round);

        session = session.Record([]);
        Assert.True(session.Exhausted);
        Assert.Equal(3, session.Rank("c"));
        Assert.Null(session.Rank("z"));
        Assert.Equal(2, session.RoundOf("c"));
    }

    [Fact]
    public void TheSystemPromptCarriesTheCatalogueAndTheAskCarriesWhatChanges()
    {
        var catalogue = Three();

        var system = FiledSearchPrompt.System(catalogue);
        var ask = FiledSearchPrompt.Ask("the plumber's quote", shownLines: [2, 3], today: new DateOnly(2026, 11, 5));

        Assert.EndsWith(catalogue.Text, system);
        Assert.Contains("at most 5", system);
        Assert.Contains("2026-11-05", ask);
        Assert.Contains("Thursday", ask);
        Assert.Contains("the plumber's quote", ask);
        Assert.Contains("Already shown: 2, 3", ask);
        Assert.Contains("Already shown: none", FiledSearchPrompt.Ask("x", [], new DateOnly(2026, 11, 5)));
    }
}
