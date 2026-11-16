using Folio.Domain.Analysis;
using Folio.Domain.Markup;

namespace Folio.Tests.Markup;

/// <summary>
/// The twelve sections that used to be pages of their own, now written on the manuscript.
///
/// <para>The rule they share is a ladder, and every rung exists because the one above it can fail
/// without anything erroring: a finding is written on <b>the passage it quoted</b>; failing that, on
/// <b>the opening of the chapter it cited</b>, saying so; failing that, it is handed back as
/// <b>unplaced</b> so the report can print it among the whole-book notes. What must never happen is
/// the fourth outcome, which is the one the old design could not have — a finding that simply stops
/// appearing because there is no longer a section to list it in.</para>
/// </summary>
public class FindingMarksTests
{
    private const string One = "Ruth came back to Saltings in the spring.\nThe water had taken her brother.\n\n";
    private const string Two = "Chapter Two\nShe walked the sea wall for an hour, and nobody spoke to her.\n\n";
    private const string Three = "Chapter Three\nThe tide was out and the boats lay on their sides.";
    private const string Manuscript = One + Two + Three;

    private static readonly IReadOnlyList<ChapterSpan> Chapters =
    [
        new(1, 0, One.Length),
        new(2, One.Length, One.Length + Two.Length),
        new(3, One.Length + Two.Length, Manuscript.Length),
    ];

    // ---- the quoted passage ---------------------------------------------------------------------

    [Theory]
    [InlineData("plot", "Plot hole")]
    [InlineData("inconsistency", "Inconsistency")]
    [InlineData("inaccuracy", "Inaccuracy")]
    [InlineData("pacing", "Pacing")]
    public void AFindingThatQuotedItsPassageIsAQueryOnThatPassage(string which, string label)
    {
        var finding = new ReportFinding("Chapter 2", "Nobody reacts to her return.") { Excerpt = "nobody spoke to her" };
        var report = MarkSource.Build(Manuscript, WithFinding(which, finding), Chapters);

        var mark = Assert.Single(report.Marks);
        Assert.Equal(MarkKind.Query, mark.Kind);
        Assert.Equal(label, mark.Label);
        Assert.Equal("nobody spoke to her", Manuscript[mark.StartOffset..mark.EndOffset]);
        Assert.Equal("Nobody reacts to her return.", mark.Reason);
        Assert.Empty(report.Unplaced);
    }

    [Fact]
    public void ADriftingPassageIsAQueryNamingTheDriftNotTheSuggestion()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            VoiceConsistency = new VoiceConsistency("Dry and spare.",
                [new VoiceDivergence("Chapter 3", "the boats lay on their sides", "Turns lyrical.", "Pull it back to the plain register.")]),
        };

        var mark = Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).Marks);

        Assert.Equal(MarkKind.Query, mark.Kind);
        Assert.Equal("Voice drift", mark.Label);
        Assert.Equal("Turns lyrical.", mark.Reason);
        Assert.DoesNotContain("plain register", mark.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AContradictionOfThePriorBookIsAQueryThatSaysWhatWasEstablished()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            SeriesContinuity = new SeriesContinuity("Salt", [
                new SeriesBreak("Chapter 2", "Her brother drowned at sea.", "The water had taken her brother.")
                    { Excerpt = "The water had taken her brother" }], []),
        };

        var mark = Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).Marks);

        Assert.Equal(MarkKind.Query, mark.Kind);
        Assert.Equal("Contradicts the previous book", mark.Label);
        Assert.Contains("The water had taken her brother.", mark.Reason, StringComparison.Ordinal);
        Assert.Contains("Her brother drowned at sea.", mark.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A continuity finding against the series bible (R22–R25) is labelled by its kind, cites the earlier
    /// book and quotes it, and says what it assumed while the book's placement is a guess.
    /// </summary>
    [Fact]
    public void AContinuityFindingNamesItsKindCitesTheEarlierBookAndSaysWhatItAssumed()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            SeriesContinuity = new SeriesContinuity("Salt", [
                new SeriesBreak("Chapter 2", "Her brother drowned at sea.", "The water had taken her brother.")
                {
                    Excerpt = "The water had taken her brother", Kind = ContinuityKinds.CharacterDrift,
                    Book = "The Salt Road", Quote = "he went under at the harbour mouth",
                }], [])
            {
                Assumption = "This assumes the book takes place after \u201CThe Salt Road\u201D, which has not been confirmed.",
            },
        };

        var mark = Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).Marks);

        Assert.Equal("Character drift", mark.Label);
        Assert.Contains("\u201Che went under at the harbour mouth\u201D (The Salt Road)", mark.Reason, StringComparison.Ordinal);
        Assert.Contains("has not been confirmed", mark.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("character drift", "Character drift")]
    [InlineData("TIMELINE ERROR", "Timeline error")]
    [InlineData("dropped thread", "Dropped thread")]
    [InlineData("contradicted fact", "Contradicted fact")]
    [InlineData("something else", "Contradicted fact")]
    [InlineData(null, "Contradicted fact")]
    public void AContinuityKindIsOneOfTheFour(string? said, string kind) =>
        Assert.Equal(kind, ContinuityKinds.Normalize(said));

    /// <summary>A scene to add is a caret — the mark for something that belongs here and is not here.</summary>
    [Fact]
    public void ASceneToAddIsACaretAtTheHeadOfThePassageItQuoted()
    {
        var addition = new SceneAddition(AdditionKind.MissingBeat, "Chapter 2", "Her first night back.", "The reader never sees her arrive.", "Must")
            { Excerpt = "She walked the sea wall for an hour" };

        var mark = Assert.Single(MarkSource.Build(Manuscript, WithAddition(addition), Chapters).Marks);

        Assert.Equal(MarkKind.Caret, mark.Kind);
        Assert.True(mark.IsPoint);
        Assert.Equal(Manuscript.IndexOf("She walked the sea wall", StringComparison.Ordinal), mark.StartOffset);
        Assert.Equal("Missing beat", mark.Label);
        Assert.Contains("Her first night back.", mark.Reason, StringComparison.Ordinal);
    }

    /// <summary>The scene's own priority is its severity: the plan does not rank additions.</summary>
    [Theory]
    [InlineData("Must", Folio.Domain.Readiness.ReadinessTier.Must)]
    [InlineData("Should", Folio.Domain.Readiness.ReadinessTier.Should)]
    [InlineData("Consider", Folio.Domain.Readiness.ReadinessTier.Consider)]
    public void ASceneToAddTakesItsOwnPriorityAsItsSeverity(string priority, Folio.Domain.Readiness.ReadinessTier tier)
    {
        var addition = new SceneAddition(AdditionKind.TensionStakes, "Chapter 2", "x", "y", priority)
            { Excerpt = "nobody spoke to her" };

        Assert.Equal(tier, Assert.Single(MarkSource.Build(Manuscript, WithAddition(addition), Chapters).Marks).Severity);
    }

    // ---- the chapter, when the quote fails ------------------------------------------------------

    /// <summary>
    /// A finding that cannot find its passage is not a query in the margin any more (R7): pinned to a
    /// chapter's opening line it read as a note about that line. It goes to a list at the chapter's
    /// head, "Notes on this chapter", which says exactly what it is.
    /// </summary>
    [Fact]
    public void AFindingWithNoQuoteGoesToItsChaptersNotesNotTheMargin()
    {
        var finding = new ReportFinding("Chapter 2", "Nobody reacts to her return.");
        var report = MarkSource.Build(Manuscript, WithFinding("plot", finding), Chapters);

        Assert.Empty(report.Marks);
        Assert.Empty(report.Unplaced);
        var chapter = Assert.Single(report.ChapterNotes);
        Assert.Equal(2, chapter.Chapter);
        var note = Assert.Single(chapter.Notes);
        Assert.Equal("Plot hole", note.Label);
        Assert.Equal("Nobody reacts to her return.", note.Text);
    }

    /// <summary>
    /// A chapter that opens with a title gets its list under the title; one that opens straight into
    /// prose gets it before the first line, rather than after the first line of a paragraph.
    /// </summary>
    [Fact]
    public void TheListFollowsAChaptersTitleAndPrecedesItsProse()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            PlotHoles = [new ReportFinding("Chapter 1", "a"), new ReportFinding("Chapter 2", "b")],
        };

        var notes = MarkSource.Build(Manuscript, analysis, Chapters).ChapterNotes;

        var first = Assert.Single(notes, n => n.Chapter == 1);
        Assert.False(first.FollowsTitle);
        Assert.Equal(0, first.Offset);

        var second = Assert.Single(notes, n => n.Chapter == 2);
        Assert.True(second.FollowsTitle);
        Assert.Equal("Chapter Two", Manuscript[One.Length..second.Offset]);
    }

    /// <summary>
    /// A misquote must not make the finding vanish. The words it tried to quote are kept in the note,
    /// because they are the only way left to find the sentence.
    /// </summary>
    [Fact]
    public void AQuoteThatIsNotInTheManuscriptFallsBackToTheChapterAndKeepsTheQuote()
    {
        var edit = new LineEdit("Chapter 3", "He mounted his horse and rode north.", "x", "Reads as a cliché.");
        var report = MarkSource.Build(Manuscript, BookAnalysisResult.Empty with { LineEdits = [edit] }, Chapters);

        Assert.Empty(report.Marks);
        var note = Assert.Single(Assert.Single(report.ChapterNotes).Notes);
        Assert.Contains("He mounted his horse and rode north.", note.Text, StringComparison.Ordinal);
        Assert.Contains("Reads as a cliché.", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AChaptersNotesAreOneListInReadingOrderMostSevereFirst()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            PlotHoles =
            [
                new ReportFinding("Chapter 2", "minor") { Severity = "Consider" },
                new ReportFinding("Chapter 2", "major") { Severity = "Must" },
                new ReportFinding("Chapter 2", "middling") { Severity = "Should" },
            ],
        };

        var chapter = Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).ChapterNotes);
        Assert.Equal(["major", "middling", "minor"], chapter.Notes.Select(note => note.Text));
    }

    [Fact]
    public void ASceneToAddNamingAChapterGetsACaretAtItsHead()
    {
        var addition = new SceneAddition(AdditionKind.CharacterDeepening, "Chapter 3", "Who she is to the village.", "y", "Should");

        var mark = Assert.Single(MarkSource.Build(Manuscript, WithAddition(addition), Chapters).Marks);

        Assert.True(mark.IsPoint);
        Assert.Equal(One.Length + Two.Length, mark.StartOffset);
    }

    /// <summary>"After Chapter 2" is the gap between two chapters, so the caret sits where the first ends.</summary>
    [Fact]
    public void ASceneToAddAfterAChapterGetsACaretAtItsClose()
    {
        var addition = new SceneAddition(AdditionKind.MissingBeat, "after Chapter 2", "The funeral.", "y", "Must");

        var mark = Assert.Single(MarkSource.Build(Manuscript, WithAddition(addition), Chapters).Marks);

        Assert.True(mark.IsPoint);
        Assert.Equal(One.Length + Two.Length, mark.StartOffset);
    }

    [Fact]
    public void AChapterTheManuscriptDoesNotHaveIsNotGuessedAt()
    {
        var finding = new ReportFinding("Chapter 40", "A thread nobody picks up.");
        var report = MarkSource.Build(Manuscript, WithFinding("plot", finding), Chapters);

        Assert.Empty(report.Marks);
        Assert.Single(report.Unplaced);
    }

    // ---- unplaced: nothing is dropped -----------------------------------------------------------

    [Fact]
    public void AFindingWithNeitherQuoteNorChapterIsHandedBackUnplaced()
    {
        var finding = new ReportFinding(string.Empty, "The ending undoes the premise.");
        var report = MarkSource.Build(Manuscript, WithFinding("inconsistency", finding), Chapters);

        Assert.Empty(report.Marks);
        var note = Assert.Single(report.Unplaced);
        Assert.Equal("Inconsistencies", note.Source);
        Assert.Equal("The ending undoes the premise.", note.Text);
    }

    [Fact]
    public void WithNoChapterListACitedFindingIsStillUnplacedNotLost()
    {
        var finding = new ReportFinding("Chapter 2", "Nobody reacts to her return.");
        var report = MarkSource.Build(Manuscript, WithFinding("plot", finding));

        Assert.Empty(report.Marks);
        Assert.Equal("Nobody reacts to her return.", Assert.Single(report.Unplaced).Text);
    }

    /// <summary>The report can be rendered with no manuscript at all; every finding then has to survive.</summary>
    [Fact]
    public void WithNoManuscriptEveryFindingIsUnplaced()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            PlotHoles = [new ReportFinding("Chapter 1", "A hole.") { Excerpt = "anything" }],
            PacingNotes = [new ReportFinding("Chapter 2", "Slack.")],
        };

        var report = MarkSource.Build(string.Empty, analysis, Chapters);

        Assert.Empty(report.Marks);
        Assert.Equal(2, report.Unplaced.Count);
    }

    /// <summary>
    /// "All three sound identical" has no passage and no single chapter. Pinning it to the chapter it
    /// happened to be noticed in would be a claim the model never made.
    /// </summary>
    [Fact]
    public void AnAggregateDialogueObservationIsUnplacedNotPinnedToAChapter()
    {
        var note = new DialogueNote("Chapter 2", "Ruth, Anna", "All three sound identical.", "Vary the register.");
        var report = MarkSource.Build(Manuscript, BookAnalysisResult.Empty with { DialogueNotes = [note] }, Chapters);

        Assert.Empty(report.Marks);
        var unplaced = Assert.Single(report.Unplaced);
        Assert.Equal("Dialogue", unplaced.Source);
        Assert.Contains("All three sound identical.", unplaced.Text, StringComparison.Ordinal);
    }

    /// <summary>A scene beat's verdict is about one scene; without its closing line there is no honest place for it.</summary>
    [Fact]
    public void ASceneBeatWithNoQuoteIsNeitherPinnedNorPrintedAsABookWideNote()
    {
        var beat = new SceneBeat("Chapter 2", "The walk", "Establishes grief.", "Earns its place.");
        var report = MarkSource.Build(Manuscript, BookAnalysisResult.Empty with { SceneBreakdown = [beat] }, Chapters);

        Assert.Empty(report.Marks);
        Assert.Empty(report.Unplaced);
    }

    // ---- the guards that already held still hold ------------------------------------------------

    /// <summary>The report-wide <c>From</c> keeps its shape for the callers and tests that only want marks.</summary>
    [Fact]
    public void FromReturnsTheSameMarksBuildDoes()
    {
        var analysis = WithFinding("plot", new ReportFinding("Chapter 2", "x") { Excerpt = "nobody spoke to her" });

        Assert.Equal(MarkSource.Build(Manuscript, analysis, null).Marks, MarkSource.From(Manuscript, analysis));
    }

    [Fact]
    public void MarksFromEverySourceComeOutInReadingOrder()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            PlotHoles = [new ReportFinding("Chapter 3", "late") { Excerpt = "the boats lay" }],
            PacingNotes = [new ReportFinding("Chapter 1", "early") { Excerpt = "Ruth came back" }],
            Inaccuracies = [new ReportFinding("Chapter 2", "middle") { Excerpt = "sea wall" }],
        };

        var offsets = MarkSource.Build(Manuscript, analysis, Chapters).Marks.Select(mark => mark.StartOffset).ToList();

        Assert.Equal(3, offsets.Count);
        Assert.Equal(offsets.OrderBy(offset => offset), offsets);
    }

    [Fact]
    public void AFindingTakesTheTierOfTheRevisionPlanItemCoveringItsChapter()
    {
        var analysis = WithFinding("plot", new ReportFinding("Chapter 2", "x") { Excerpt = "nobody spoke to her" }) with
        {
            RevisionPlan = [new RevisionItem("Must", "Ch. 1-3", "The opening does not commit", "x")],
        };

        Assert.Equal(Folio.Domain.Readiness.ReadinessTier.Must,
            Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).Marks).Severity);
    }

    // ---- severity (R8) -------------------------------------------------------------------------

    /// <summary>
    /// A finding's own severity, asked for in the prompt that produced it, outranks the chapter proxy:
    /// a small slip inside a chapter a Must covers is not itself a Must.
    /// </summary>
    [Theory]
    [InlineData("Must", Folio.Domain.Readiness.ReadinessTier.Must)]
    [InlineData("should", Folio.Domain.Readiness.ReadinessTier.Should)]
    [InlineData("Consider", Folio.Domain.Readiness.ReadinessTier.Consider)]
    public void AFindingsOwnSeverityOutranksTheRevisionPlansChapter(string severity, Folio.Domain.Readiness.ReadinessTier tier)
    {
        var analysis = WithFinding("plot", new ReportFinding("Chapter 2", "x") { Excerpt = "nobody spoke to her", Severity = severity }) with
        {
            RevisionPlan = [new RevisionItem("Must", "Ch. 1-3", "The opening does not commit", "x")],
        };

        Assert.Equal(tier, Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).Marks).Severity);
    }

    [Fact]
    public void EveryKindOfNoteCarriesItsOwnSeverity()
    {
        const string Must = "Must";
        var analysis = BookAnalysisResult.Empty with
        {
            LineEdits = [new LineEdit("Chapter 2", "nobody spoke to her", "x", "r") { Severity = Must }],
            ProseNotes = [new ProseNote("Chapter 3", "p", "The tide was out", "f") { Severity = Must }],
            DialogueNotes = [new DialogueNote("Chapter 2", "s", "o", "f") { Excerpt = "She walked the sea wall", Severity = Must }],
            VoiceConsistency = new VoiceConsistency("v", [new VoiceDivergence("Chapter 3", "the boats lay on their sides", "d", "s") { Severity = Must }]),
            SeriesContinuity = new SeriesContinuity("Book One",
                [new SeriesBreak("Chapter 1", "e", "c") { Excerpt = "Ruth came back", Severity = Must }], []),
            Expansion = new ExpansionReport([], [new ShowTellNote("Chapter 1", "The water had taken her brother", ShowTellDirection.Dramatize, "s") { Severity = Must }], LengthAssessment.Empty),
        };

        var marks = MarkSource.Build(Manuscript, analysis, Chapters).Marks;

        Assert.Equal(6, marks.Count);
        Assert.All(marks, mark => Assert.Equal(Folio.Domain.Readiness.ReadinessTier.Must, mark.Severity));
    }

    [Fact]
    public void ASceneInconsistencyCarriesItsOwnSeverity()
    {
        var analysis = BookAnalysisResult.Empty with
        {
            SceneJudgements =
            [
                new Folio.Domain.Scenes.SceneJudgement(1, "Chapter 2", "a", Folio.Domain.Scenes.SceneCall.Keep, 0.5, "v",
                    [new Folio.Domain.Scenes.SceneInconsistency(Folio.Domain.Scenes.SceneInconsistencyKind.Tense, "d", "nobody spoke to her") { Severity = "Should" }]),
            ],
        };

        Assert.Equal(Folio.Domain.Readiness.ReadinessTier.Should,
            Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).Marks).Severity);
    }

    [Fact]
    public void ASeverityTheModelDidNotSendFallsBackToTheChapterProxy()
    {
        var analysis = WithFinding("plot", new ReportFinding("Chapter 2", "x") { Excerpt = "nobody spoke to her", Severity = "Urgent!" }) with
        {
            RevisionPlan = [new RevisionItem("Should", "Ch. 2", "t", "x")],
        };

        Assert.Equal(Folio.Domain.Readiness.ReadinessTier.Should,
            Assert.Single(MarkSource.Build(Manuscript, analysis, Chapters).Marks).Severity);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static BookAnalysisResult WithFinding(string which, ReportFinding finding) => which switch
    {
        "plot" => BookAnalysisResult.Empty with { PlotHoles = [finding] },
        "inconsistency" => BookAnalysisResult.Empty with { Inconsistencies = [finding] },
        "inaccuracy" => BookAnalysisResult.Empty with { Inaccuracies = [finding] },
        "pacing" => BookAnalysisResult.Empty with { PacingNotes = [finding] },
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    private static BookAnalysisResult WithAddition(SceneAddition addition) =>
        BookAnalysisResult.Empty with { Expansion = new ExpansionReport([addition], [], LengthAssessment.Empty) };
}
