using Folio.Domain.Analysis;
using Folio.Domain.Markup;
using Folio.Domain.Readiness;

namespace Folio.Tests.Markup;

/// <summary>
/// Where a mark comes from, and the failure that makes the whole marked-up manuscript worthless
/// without saying anything.
///
/// <para>The bug this reintroduces is searching for an excerpt literally. A quotation comes back
/// through a model and a JSON string: the manuscript's line break is a space in the excerpt, runs of
/// spaces collapse, and a typographic quote has often become a straight one. Searched as-is, most
/// excerpts are not found and the document prints almost no marks — which reads as a clean draft
/// rather than as a search that failed. Nothing errors, and the report is still 140 pages long.</para>
/// </summary>
public class MarkSourceTests
{
    private const string Manuscript =
        "Ruth came back to Saltings in the spring.\nThe water had taken her brother and the village "
        + "had let it.\n\nShe walked the sea wall for an hour, and nobody spoke to her, and she "
        + "understood that nothing had been forgiven.";

    [Fact]
    public void ALineEditBecomesAQueryOverTheSentenceItQuoted()
    {
        var analysis = With(lineEdits: [new LineEdit("Ch. 1", "Ruth came back to Saltings in the spring.", "x", "Flat opening.")]);

        var mark = Assert.Single(MarkSource.From(Manuscript, analysis));
        Assert.Equal(MarkKind.Query, mark.Kind);
        Assert.Equal("Ruth came back to Saltings in the spring.", Manuscript[mark.StartOffset..mark.EndOffset]);
        Assert.Equal("Flat opening.", mark.Reason);
    }

    /// <summary>
    /// The label names the actual defect — a filter word, a redundant clause, a cliché — rather than
    /// the single "Unclear" every line edit used to carry regardless of what was really wrong. A model
    /// reply (or a report saved before <see cref="LineEditKind"/> existed) that never sets it degrades
    /// to a plain "Line edit" tag rather than a false claim of unclarity.
    /// </summary>
    [Theory]
    [InlineData(LineEditKind.FilterWord, "Filter word")]
    [InlineData(LineEditKind.Redundant, "Redundant")]
    [InlineData(LineEditKind.WeakWord, "Weak word")]
    [InlineData(LineEditKind.Cliche, "Cliché")]
    [InlineData(LineEditKind.Grammar, "Grammar")]
    [InlineData(LineEditKind.Digression, "Digression")]
    [InlineData(LineEditKind.Tone, "Tone")]
    [InlineData(LineEditKind.Other, "Line edit")]
    public void TheLabelNamesWhatIsActuallyWrong(LineEditKind kind, string label)
    {
        var analysis = With(lineEdits:
            [new LineEdit("Ch. 1", "Ruth came back to Saltings in the spring.", "x", "y") { Kind = kind }]);

        Assert.Equal(label, Assert.Single(MarkSource.From(Manuscript, analysis)).Label);
    }

    /// <summary>
    /// The one that matters. The excerpt spans the manuscript's own line break, arrives with it
    /// flattened to a space, and carries a curly apostrophe the page does not have.
    /// </summary>
    [Fact]
    public void AnExcerptWhoseWhitespaceAndQuotesChangedIsStillFound()
    {
        const string asQuoted = "in the spring. The water had taken her brother";
        var analysis = With(lineEdits: [new LineEdit("Ch. 1", asQuoted, "x", "Two beats in one.")]);

        var mark = Assert.Single(MarkSource.From(Manuscript, analysis));
        Assert.Equal("in the spring.\nThe water had taken her brother", Manuscript[mark.StartOffset..mark.EndOffset]);
    }

    /// <summary>
    /// The other half, and it hides behind the first: a manuscript double-spaced after its full
    /// stops still matches an excerpt that came back single-spaced. Folding whitespace to one space
    /// is not enough — the runs have to collapse on both sides.
    /// </summary>
    [Fact]
    public void AManuscriptDoubleSpacedAfterItsStopsStillMatches()
    {
        const string typed = "She stopped at the wall.  Nobody spoke to her.  The tide was out.";
        var analysis = With(lineEdits: [new LineEdit("Ch. 1", "the wall. Nobody spoke to her.", "x", "y")]);

        var mark = Assert.Single(MarkSource.From(typed, analysis));
        Assert.Equal("the wall.  Nobody spoke to her.", typed[mark.StartOffset..mark.EndOffset]);
    }

    /// <summary>A curly apostrophe on the page and a straight one in the excerpt are the same word.</summary>
    [Fact]
    public void ACurlyApostropheOnThePageMatchesAStraightOneInTheExcerpt()
    {
        const string typed = "It was her brother\u2019s coat, and she knew it.";
        var analysis = With(lineEdits: [new LineEdit("Ch. 1", "her brother's coat", "x", "y")]);

        Assert.Single(MarkSource.From(typed, analysis));
    }

    [Fact]
    public void AnExcerptThatIsNotInTheManuscriptIsDroppedRatherThanPlaced()
    {
        var analysis = With(lineEdits: [new LineEdit("Ch. 1", "He mounted his horse and rode north.", "x", "y")]);

        Assert.Empty(MarkSource.From(Manuscript, analysis));
    }

    /// <summary>
    /// A prose-style note is line-by-line — a filter word, a tic — so it anchors to the text like a
    /// line edit does, using the same verbatim quote the report already prints as "evidence".
    /// </summary>
    [Fact]
    public void AProseNoteBecomesAQueryOverItsEvidence()
    {
        var analysis = With(prose: [new ProseNote("Ch. 1", "Filter words", "she understood", "Cut the filter.")]);

        var mark = Assert.Single(MarkSource.From(Manuscript, analysis));
        Assert.Equal(MarkKind.Query, mark.Kind);
        Assert.Equal("Prose", mark.Label);
        Assert.Equal("she understood", Manuscript[mark.StartOffset..mark.EndOffset]);
        Assert.Equal("Cut the filter.", mark.Reason);
    }

    /// <summary>
    /// A dialogue note with a real quoted exchange anchors to it, like a prose note. One with no
    /// excerpt — an aggregate observation such as "all three sound identical" — has nothing to anchor
    /// to and produces no mark; it stays a bullet under the Dialogue section instead.
    /// </summary>
    [Fact]
    public void ADialogueNoteWithAQuotedExchangeBecomesAQuery()
    {
        var analysis = With(dialogue:
            [new DialogueNote("Ch. 1", "Ruth", "On the nose.", "Trust the subtext.") { Excerpt = "nobody spoke to her" }]);

        var mark = Assert.Single(MarkSource.From(Manuscript, analysis));
        Assert.Equal(MarkKind.Query, mark.Kind);
        Assert.Equal("Dialogue", mark.Label);
        Assert.Equal("nobody spoke to her", Manuscript[mark.StartOffset..mark.EndOffset]);
    }

    [Fact]
    public void ADialogueNoteWithNoExcerptProducesNoMark()
    {
        var analysis = With(dialogue: [new DialogueNote("Ch. 1", "Ruth, Anna", "All three sound identical.", "Vary the register.")]);

        Assert.Empty(MarkSource.From(Manuscript, analysis));
    }

    /// <summary>
    /// A scene that earns its place is praise — the first real source for the Check mark, which has
    /// stood in the vocabulary since Release G with nothing yet producing it. One that does no work
    /// is struck through, the same as a passage that runs long. Both sit at the scene's own close.
    /// </summary>
    [Fact]
    public void ASceneThatEarnsItsPlaceIsACheck()
    {
        var analysis = With(sceneBreakdown:
            [new SceneBeat("Ch. 1", "The walk", "Establishes grief.", "Earns its place.")
                { Excerpt = "nobody spoke to her", Outcome = SceneVerdict.EarnsItsPlace }]);

        var mark = Assert.Single(MarkSource.From(Manuscript, analysis));
        Assert.Equal(MarkKind.Check, mark.Kind);
    }

    [Fact]
    public void ASceneThatDoesNoWorkIsADele()
    {
        var analysis = With(sceneBreakdown:
            [new SceneBeat("Ch. 1", "The walk", "Establishes grief.", "Does no work.")
                { Excerpt = "nobody spoke to her", Outcome = SceneVerdict.DoesNoWork }]);

        var mark = Assert.Single(MarkSource.From(Manuscript, analysis));
        Assert.Equal(MarkKind.Dele, mark.Kind);
    }

    [Fact]
    public void ASceneBeatWithNoExcerptProducesNoMark()
    {
        var analysis = With(sceneBreakdown: [new SceneBeat("Ch. 1", "The walk", "Establishes grief.", "Earns its place.")]);

        Assert.Empty(MarkSource.From(Manuscript, analysis));
    }

    [Fact]
    public void APassageThatRunsLongIsADeleAndASummarizedOneIsACaret()
    {
        var analysis = With(showTell:
        [
            new ShowTellNote("Ch. 1", "She walked the sea wall for an hour", ShowTellDirection.Tighten, "This runs long."),
            new ShowTellNote("Ch. 1", "nobody spoke to her", ShowTellDirection.Dramatize, "This could be a scene."),
        ]);

        var marks = MarkSource.From(Manuscript, analysis);

        Assert.Equal(MarkKind.Dele, marks.Single(m => m.Label == "Runs long").Kind);
        Assert.Equal(MarkKind.Caret, marks.Single(m => m.Label == "Summarized").Kind);
    }

    /// <summary>
    /// A caret is a point at the head of the passage, not a run over it. Found on the page: drawn
    /// over the whole quotation it prints once per line of it, which reads as several missing things
    /// rather than the one thing that is missing.
    /// </summary>
    [Fact]
    public void ACaretIsAPointAtTheHeadOfThePassage()
    {
        var analysis = With(showTell:
            [new ShowTellNote("Ch. 1", "nobody spoke to her", ShowTellDirection.Dramatize, "z")]);

        var caret = Assert.Single(MarkSource.From(Manuscript, analysis));

        Assert.True(caret.IsPoint);
        Assert.Equal(Manuscript.IndexOf("nobody spoke to her", StringComparison.Ordinal), caret.StartOffset);
    }

    /// <summary>A dele keeps its run: it has to strike the words it is taking out.</summary>
    [Fact]
    public void ADeleKeepsTheWholePassageItStrikes()
    {
        var analysis = With(showTell:
            [new ShowTellNote("Ch. 1", "She walked the sea wall for an hour", ShowTellDirection.Tighten, "z")]);

        var dele = Assert.Single(MarkSource.From(Manuscript, analysis));

        Assert.False(dele.IsPoint);
        Assert.Equal("She walked the sea wall for an hour", Manuscript[dele.StartOffset..dele.EndOffset]);
    }

    /// <summary>
    /// F40 at the boundary where it is actually enforced: both source records carry a suggestion,
    /// and the mark carries the reason only. Nothing drawn on the manuscript hands the author prose.
    /// </summary>
    [Fact]
    public void TheSourcesSuggestionNeverReachesTheMark()
    {
        var analysis = With(lineEdits:
            [new LineEdit("Ch. 1", "Ruth came back to Saltings in the spring.", "Try: Ruth returned to Saltings.", "Flat opening.")]);

        var mark = Assert.Single(MarkSource.From(Manuscript, analysis));

        Assert.DoesNotContain("Ruth returned", mark.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Ruth returned", mark.Label, StringComparison.Ordinal);
    }

    /// <summary>A mark inside the chapters a Must covers is a Must; nothing else raises it.</summary>
    [Fact]
    public void AMarkTakesTheTierOfTheRevisionPlanItemCoveringItsChapter()
    {
        var analysis = With(
            lineEdits: [new LineEdit("Ch. 1", "Ruth came back to Saltings in the spring.", "x", "y")],
            plan: [new RevisionItem("Must", "Ch. 1-3", "The opening does not commit", "x")]);

        Assert.Equal(ReadinessTier.Must, Assert.Single(MarkSource.From(Manuscript, analysis)).Severity);
    }

    [Fact]
    public void AMarkNoPlanItemCoversStaysAtConsider()
    {
        var analysis = With(
            lineEdits: [new LineEdit("Ch. 1", "Ruth came back to Saltings in the spring.", "x", "y")],
            plan: [new RevisionItem("Must", "Ch. 8-9", "The close hurries", "x")]);

        Assert.Equal(ReadinessTier.Consider, Assert.Single(MarkSource.From(Manuscript, analysis)).Severity);
    }

    [Fact]
    public void MarksComeOutInReadingOrder()
    {
        var analysis = With(lineEdits:
        [
            new LineEdit("Ch. 1", "nobody spoke to her", "x", "y"),
            new LineEdit("Ch. 1", "Ruth came back to Saltings", "x", "y"),
        ]);

        var marks = MarkSource.From(Manuscript, analysis);

        Assert.Equal(marks.Select(m => m.StartOffset).OrderBy(o => o), marks.Select(m => m.StartOffset));
    }

    /// <summary>
    /// Neither a line edit nor a show-vs-tell note is ever praise — a check comes only from a scene
    /// that earns its place (see <see cref="ASceneThatEarnsItsPlaceIsACheck"/>), the first real
    /// source for a mark that sat drawn and tested but unreachable from Release G through this.
    /// </summary>
    [Fact]
    public void ALineEditOrShowTellNoteNeverProducesACheck()
    {
        var analysis = With(
            lineEdits: [new LineEdit("Ch. 1", "Ruth came back to Saltings in the spring.", "x", "y")],
            showTell: [new ShowTellNote("Ch. 1", "nobody spoke to her", ShowTellDirection.Dramatize, "z")]);

        Assert.DoesNotContain(MarkSource.From(Manuscript, analysis), mark => mark.Kind == MarkKind.Check);
    }

    private static BookAnalysisResult With(
        IReadOnlyList<LineEdit>? lineEdits = null,
        IReadOnlyList<ShowTellNote>? showTell = null,
        IReadOnlyList<RevisionItem>? plan = null,
        IReadOnlyList<ProseNote>? prose = null,
        IReadOnlyList<DialogueNote>? dialogue = null,
        IReadOnlyList<SceneBeat>? sceneBreakdown = null) =>
        BookAnalysisResult.Empty with
        {
            LineEdits = lineEdits ?? [],
            RevisionPlan = plan ?? [],
            Expansion = new ExpansionReport([], showTell ?? [], LengthAssessment.Empty),
            ProseNotes = prose ?? [],
            DialogueNotes = dialogue ?? [],
            SceneBreakdown = sceneBreakdown ?? [],
        };
}
