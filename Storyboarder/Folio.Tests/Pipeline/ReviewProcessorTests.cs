using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PdfSharp.Pdf.IO;
using Folio.Domain.Analysis;
using Folio.Domain.Reading;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Domain.Pipeline;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.External;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Pipeline;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Storyworthtelling.Paper;
using Storyworthtelling.Remarkable;
using Xunit;

namespace Folio.Tests.Pipeline;

// End-to-end coverage of the Folio review pipeline: ingest → design bible (+ reference sheets) →
// per-scene images (none until scene detection is wired) → the map/reduce revision report → archive.
// External services are faked; the report PDF is produced on disk and opens as a valid PDF.
public class ReviewProcessorTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "canonizer-e2e", Guid.NewGuid().ToString("N"));

    private readonly Mock<IClaudeAnalysisClient> claude = new();
    private readonly Mock<IGeminiImageClient> gemini = new();
    private int chapterAnalysisCalls;
    private readonly List<string> sectionReads = [];
    private readonly List<string> readerReads = [];
    private readonly List<string> synthesisReads = [];
    private int sectionsSeenBySynthesis;
    private int readersSeenBySynthesis;

    public ReviewProcessorTests()
    {
        claude.Setup(c => c.ExtractEntities(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesignBibleFragment(
                [new EntityMention("Hero", "a brave knight in silver armor")],
                [new EntityMention("Castle", "a grey stone fortress")]));

        claude.Setup(c => c.DeriveArtStyle(It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ink-wash watercolor, muted palette");

        // The sixteen reads. Stubbed for real rather than left to Moq's defaults: a default-valued
        // Task<T> completes with null, which the pipeline records as a finished pass — so the step
        // would appear to work while reading nothing, which is the failure PassPipelineTests names.
        claude.Setup(c => c.ReadSection(It.IsAny<Persona>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, string _, CancellationToken _) =>
            {
                sectionReads.Add(persona.Id);
                return Task.FromResult(new SectionRead(
                    persona.Id, $"{persona.Name}: one thing to report.",
                    [new ReportFinding("Chapter 1", $"{persona.Name} found something.")]));
            });

        claude.Setup(c => c.ReadAsReader(It.IsAny<Persona>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, string _, CancellationToken _) =>
            {
                readerReads.Add(persona.Id);
                return Task.FromResult(new Review(
                    persona.Id, "A verdict in one clause.", "A sustained read in character.",
                    ["what works"], ["what does not"], "what it would need"));
            });

        claude.Setup(c => c.Synthesise(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<SectionRead>>(), It.IsAny<IReadOnlyList<Review>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string passId, IReadOnlyList<SectionRead> sections, IReadOnlyList<Review> readers, int _, CancellationToken _) =>
            {
                synthesisReads.Add(passId);
                sectionsSeenBySynthesis = sections.Count;
                readersSeenBySynthesis = readers.Count;
                return Task.FromResult(passId == "verdict"
                    ? new SynthesisRead([], new Verdict(["a strength"], ["a problem"], "the middle compressed"))
                    : new SynthesisRead([new RevisionItem("Must", "Ch. 12", "The middle", "Compress it.")], Verdict.Empty));
            });

        // The first chapter-analysis call is rate-limited; the worker must reschedule and succeed on retry.
        claude.Setup(c => c.AnalyzeChapter(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string chapterLabel, IReadOnlyList<string> _, string _, CancellationToken _) =>
            {
                chapterAnalysisCalls++;
                return chapterAnalysisCalls == 1
                    ? Task.FromException<ChapterNotes>(new RateLimitException("429 from test"))
                    : Task.FromResult(new ChapterNotes(
                        chapterLabel,
                        ["Hero"], ["Hero walks to the castle"], ["Hero resolves to enter"], [], [], [], ["Vivid setting"], [],
                        ["\"He felt the cold.\" — cut the filter"], ["Filter words recur"], ["Hero sounds like Villain"],
                        ["The hero knocks | opens the confrontation | earns its place"])
                    {
                        RunningState = $"Story so far through {chapterLabel}: the hero reached the castle."
                    });
            });

        claude.Setup(c => c.ConsolidateAnalysis(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookAnalysisResult(
                "A hero reaches a castle.", "The hero journeys to the castle and knocks.",
                [new CharacterProfile("Hero", "protagonist", "Grows from timid to resolute.", ["approaches: Castle"])],
                ["perseverance"],
                [new ReportFinding("Chapter 1", "How did the gate open?")],
                [new ReportFinding("Chapter 1", "Eye colour shifts")],
                [new ReportFinding("Chapter 1", "Castle geography")],
                [new ReportFinding("Chapter 1", "Slow open")],
                [new Review("developmental-editor", "Promising but uneven.", "A sustained read of the structure.",
                    ["Voice"], ["Pacing"], "The opening tightened.")],
                [new RevisionItem("High", "Chapter 1", "Sagging middle", "Cut a subplot.")],
                ["Comparable Title"], "Upmarket fantasy for adult readers.",
                // The craft sections arrive from ConsolidateCraft and are folded in by the processor.
                [], [], [], []));

        claude.Setup(c => c.ConsolidateCraft(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CraftReport(
                [new LineEdit("Chapter 1", "He felt the cold.", "The cold came.", "Filtered.")],
                [new ProseNote("Chapter 1", "Filter words", "\"he felt\"", "Cut the filter.")],
                [new DialogueNote("Chapter 1", "Hero", "On the nose.", "Trust the subtext.")],
                [new SceneBeat("Chapter 1", "The hero knocks.", "Opens the confrontation.", "Earns its place.")]));

        claude.Setup(c => c.AssessVoiceConsistency(
                It.IsAny<IReadOnlyList<ChapterSample>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VoiceConsistency.Empty);

        // Default reconciliation: treat every current finding as new (no prior-thread match). Tests that care
        // about carry/regress set their own reconciliation.
        claude.Setup(c => c.ReconcileFindings(
                It.IsAny<IReadOnlyList<PriorFinding>>(), It.IsAny<IReadOnlyList<CandidateFinding>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<PriorFinding> _, IReadOnlyList<CandidateFinding> candidates, CancellationToken _) =>
                Task.FromResult(new FindingReconciliation(candidates.Select(c => new FindingMatch(c.Index, null)).ToList())));

        // Default expansion read: a small additive suggestion + a genre range, so the folded report exercises
        // the "Scenes to add"/"Show vs tell"/length path.
        claude.Setup(c => c.ProposeExpansion(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<ChapterNotes> _, string _, int wordCount, string _, CancellationToken _) =>
                Task.FromResult(new ExpansionReport(
                    [new SceneAddition(AdditionKind.CharacterDeepening, "Chapter 1", "the reader never sees her grieve", "the loss lands flat", "Should")],
                    [new ShowTellNote("Chapter 1", "Years passed.", ShowTellDirection.Dramatize, "this leap could be a scene")],
                    new LengthAssessment(wordCount, 90_000, 100_000, LengthGap.Describe(wordCount, 90_000, 100_000)))));

        // Default cross-book continuity read: nothing to flag (a standalone book never calls this; a sequel with
        // this default folds an empty — and therefore unrendered — series section). Tests that care set their own.
        claude.Setup(c => c.AssessSeriesContinuity(
                It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<IReadOnlyList<ChapterNotes>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, SeriesCanon canon, IReadOnlyList<ChapterNotes> _, string _, CancellationToken _) =>
                Task.FromResult(SeriesContinuity.Empty with { PriorBookTitle = canon.PriorBookTitle }));

        claude.Setup(c => c.DescribeSceneImage(
                It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A knight stands before a grey castle gate at dusk.");

        gemini.Setup(g => g.GenerateReferenceSheet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());

        gemini.Setup(g => g.GenerateSceneImage(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());

        gemini.Setup(g => g.GenerateJacket(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private ReviewProcessor NewProcessor(
        ArchiveOptions? archiveOptions = null, IRemarkableClient? remarkable = null,
        IPaperDriveArchive? driveArchive = null, ILogger<ReviewProcessor>? logger = null) => new(
        database,
        claude.Object,
        gemini.Object,
        new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
        new AnalysisPdfComposer(new AnalysisDocumentRenderer()),
        clock,
        Options.Create(new RateLimitOptions
        {
            ClaudeRequestsPerMinute = 1000, ClaudeRequestsPerDay = 100000,
            GeminiRequestsPerMinute = 1000, GeminiRequestsPerDay = 100000,
            BaseDelaySeconds = 4, MaxDelaySeconds = 60, MaxAttempts = 6
        }),
        Options.Create(archiveOptions ?? new ArchiveOptions()),
        logger ?? NullLogger<ReviewProcessor>.Instance,
        delivery: remarkable is null && driveArchive is null
            ? null
            : new PaperDelivery(remarkable, driveArchive));

    private Guid SeedManuscript(string? fullText = null)
    {
        var text = fullText ?? "A short tale. The hero walked to the castle and knocked on the gate.";
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc",
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        novel.ProjectId = novel.Id;
        novel.Source = new SourceText { NovelId = novel.Id, FullText = text, CharacterCount = text.Length };

        using var context = database.NewContext();
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    // Drains the pipeline the way the worker does, advancing the clock past any backoff wait so a
    // rescheduled unit becomes due. Bails out after a generous cycle cap so a stuck test fails fast.
    /// <summary>
    /// End to end: all sixteen reads run, each once, in plan order — and the synthesis passes see
    /// the other fourteen, which is the only thing that makes them synthesis rather than a
    /// fifteenth opinion.
    /// </summary>
    [Fact]
    public async Task EverySixteenReadsRunOnceAndTheSynthesisSeesThemAll()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        Assert.Equal(
            PersonaCatalogue.Sections.Select(persona => persona.Id).ToList(), sectionReads);
        Assert.Equal(
            PersonaCatalogue.Readers.Select(persona => persona.Id).ToList(), readerReads);
        Assert.Equal(["todo", "verdict"], synthesisReads);

        Assert.Equal(8, sectionsSeenBySynthesis);
        Assert.Equal(6, readersSeenBySynthesis);

        // One row per pass, all settled, so a re-run bills nothing again.
        using var context = database.NewContext();
        var rows = context.PassReads.Where(pass => pass.NovelId == id).ToList();
        Assert.Equal(16, rows.Count);
        Assert.All(rows, row => Assert.NotNull(row.Json));
    }

    private async Task DriveToCompletion(ReviewProcessor processor)
    {
        for (var cycle = 0; cycle < 200; cycle++)
        {
            var result = await processor.ProcessNextAsync(CancellationToken.None);
            if (result.Outcome == ProcessOutcome.Idle)
            {
                using var context = database.NewContext();
                if (context.Novels.All(n => n.Stage == ProjectStage.Completed || n.Stage == ProjectStage.Failed))
                    return;
            }
            if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        }

        Assert.Fail("The pipeline did not settle within the cycle budget.");
    }


    // ---- A failing whole-manuscript call must be visible, paced, and capped --------------------------
    //
    // The sixteen reads and the reduce calls each send the whole book to Claude. A failure used to be
    // recorded nowhere but the manuscript's one-line error, was retried four seconds later with no limit
    // (StageAttempts documented a cap that nothing applied), and the next worker cycle started again
    // immediately — so a call that kept dying mid-stream was re-billed all day with nothing in the log.

    private static ReadingPass FirstPass => ReadingPlan.Passes[0];

    private void FailPass(ReadingPass pass, Func<int, Exception?> failure, List<int>? calls = null)
    {
        var count = 0;
        claude.Setup(c => c.ReadSection(
                It.Is<Persona>(persona => persona.Id == pass.Id), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, string _, CancellationToken _) =>
            {
                count++;
                calls?.Add(count);
                return failure(count) is { } error
                    ? Task.FromException<SectionRead>(error)
                    : Task.FromResult(new SectionRead(
                        persona.Id, $"{persona.Name}: one thing to report.",
                        [new ReportFinding("Chapter 1", $"{persona.Name} found something.")]));
            });
    }

    private static Exception ConnectionAbort() => new HttpRequestException(
        "Error while copying content to a stream.", new IOException("Software caused connection abort"));

    private async Task Cycle(ReviewProcessor processor, int cycles, bool advance = true)
    {
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var result = await processor.ProcessNextAsync(CancellationToken.None);
            if (advance && result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        }
    }

    // Steps the pipeline until the failing pass has been called once. The clock moves only for waits
    // that come BEFORE that call, so the test then holds it still at the moment of the failure.
    private async Task<ProcessResult> DriveToFirstCall(ReviewProcessor processor, List<int> calls)
    {
        var result = ProcessResult.Idle;
        for (var cycle = 0; cycle < 200 && calls.Count == 0; cycle++)
        {
            result = await processor.ProcessNextAsync(CancellationToken.None);
            if (calls.Count == 0 && result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        }
        Assert.NotEmpty(calls);
        return result;
    }

    private Novel LoadNovel(Guid id)
    {
        using var context = database.NewContext();
        return context.Novels.Single(n => n.Id == id);
    }

    [Fact]
    public async Task AFailedReadingPassIsLoggedWithItsNameItsAttemptAndItsRootCause()
    {
        SeedManuscript();
        FailPass(FirstPass, _ => ConnectionAbort());
        var logger = new CapturingLogger<ReviewProcessor>();

        await Cycle(NewProcessor(logger: logger), 60);

        var failure = logger.Entries.First(e => e.Level == LogLevel.Warning && e.Message.Contains(FirstPass.Id));
        Assert.Contains("attempt 1/6", failure.Message);
        Assert.Contains("Software caused connection abort", failure.Message);
        Assert.IsType<HttpRequestException>(failure.Exception);
    }

    [Fact]
    public async Task AReduceCallThatFailsIsLoggedByNameToo()
    {
        SeedManuscript();
        claude.Setup(c => c.ConsolidateAnalysis(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<DesignBible>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Anthropic request failed: 400 (BadRequest)"));
        var logger = new CapturingLogger<ReviewProcessor>();

        await Cycle(NewProcessor(logger: logger), 60);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains(nameof(IClaudeAnalysisClient.ConsolidateAnalysis)) &&
            e.Message.Contains("400 (BadRequest)"));
    }

    [Fact]
    public async Task AReadThatKeepsFailingIsTriedOnlyAsOftenAsTheAttemptBudgetAllows()
    {
        var id = SeedManuscript();
        var calls = new List<int>();
        FailPass(FirstPass, _ => ConnectionAbort(), calls);
        var logger = new CapturingLogger<ReviewProcessor>();

        // Far more cycles, and far more waiting, than six attempts need.
        await Cycle(NewProcessor(logger: logger), 200);

        Assert.Equal(6, calls.Count);
        var novel = LoadNovel(id);
        Assert.Equal(ProjectStage.ComposingAnalysis, novel.Stage);
        Assert.Equal(6, novel.StageAttempts);
        Assert.Contains(FirstPass.Id, novel.LastError);
        Assert.Contains("Software caused connection abort", novel.LastError);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("giving up"));
    }

    [Fact]
    public async Task ARetryWaitsForTheBackoffEvenWhenTheWorkerStartsAFreshCycle()
    {
        var id = SeedManuscript();
        var calls = new List<int>();
        FailPass(FirstPass, _ => ConnectionAbort(), calls);

        // Drive to the first failure, without letting the clock move past the wait that follows it.
        var processor = NewProcessor();
        var result = await DriveToFirstCall(processor, calls);
        var callsAfterFailure = calls.Count;

        // A new worker cycle is a new processor; the wait must live in the database, not in memory.
        for (var cycle = 0; cycle < 5; cycle++)
        {
            result = await NewProcessor().ProcessNextAsync(CancellationToken.None);
            Assert.Equal(ProcessOutcome.Waiting, result.Outcome);
        }
        Assert.Equal(callsAfterFailure, calls.Count);
        Assert.NotNull(LoadNovel(id).RetryNotBeforeUtc);

        clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        await NewProcessor().ProcessNextAsync(CancellationToken.None);
        Assert.Equal(callsAfterFailure + 1, calls.Count);
    }

    [Fact]
    public async Task EachFailureWaitsLongerThanTheOneBefore()
    {
        var id = SeedManuscript();
        var calls = new List<int>();
        FailPass(FirstPass, _ => ConnectionAbort(), calls);
        var processor = NewProcessor();

        var waits = new List<TimeSpan>();
        for (var cycle = 0; cycle < 100 && waits.Count < 4; cycle++)
        {
            var before = calls.Count;
            var result = await processor.ProcessNextAsync(CancellationToken.None);
            if (calls.Count > before && result.Outcome == ProcessOutcome.Waiting)
            {
                waits.Add(result.Wait);
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
            }
            else if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        }

        Assert.Equal(4, waits.Count);
        Assert.True(waits[3] > waits[0] * 4, $"Waits did not grow: {string.Join(", ", waits)}");
        Assert.Equal(4, LoadNovel(id).StageAttempts);
    }

    [Fact]
    public async Task ASuccessAfterFailuresClearsTheBudgetAndTheBookCompletes()
    {
        var id = SeedManuscript();
        FailPass(FirstPass, attempt => attempt <= 2 ? ConnectionAbort() : null);

        await DriveToCompletion(NewProcessor());

        var novel = LoadNovel(id);
        Assert.Equal(ProjectStage.Completed, novel.Stage);
        Assert.Equal(0, novel.StageAttempts);
        Assert.Null(novel.LastError);
        Assert.Null(novel.RetryNotBeforeUtc);
    }

    [Fact]
    public async Task ARateLimitNeverSpendsTheAttemptBudget()
    {
        var id = SeedManuscript();
        FailPass(FirstPass, attempt => attempt <= 10 ? new RateLimitException("429 from test") : null);

        await DriveToCompletion(NewProcessor());

        Assert.Equal(ProjectStage.Completed, LoadNovel(id).Stage);
    }

    [Fact]
    public async Task ExhaustedCreditsWaitTheLongFlatIntervalAndAreLogged()
    {
        var id = SeedManuscript();
        var calls = new List<int>();
        FailPass(FirstPass,
            _ => new RateLimitException("Your credit balance is too low", kind: RateLimitKind.QuotaExhausted), calls);
        var logger = new CapturingLogger<ReviewProcessor>();
        var processor = NewProcessor(logger: logger);

        await DriveToFirstCall(processor, calls);
        var result = await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.Waiting, result.Outcome);
        Assert.True(result.Wait >= TimeSpan.FromSeconds(50), $"Waited only {result.Wait}");
        Assert.Contains(logger.Entries, e => e.Message.Contains("credit balance"));
        Assert.Equal(0, LoadNovel(id).StageAttempts);
    }


    // ---- No network is not a failure of the call --------------------------------------------------------
    //
    // A phone that has lost its connection fails every request instantly, naming the host it could not
    // look up. Nothing was sent and nothing billed, so it must not use up the attempts that exist to stop
    // a billed call being repeated: on a real book it spent attempts 1 and 2 of five chunks within ten
    // seconds, with the phone simply offline.

    private static Exception Offline() => new HttpRequestException("Connection failure",
        new IOException("Unable to resolve host \"api.anthropic.com\": No address associated with hostname"));

    [Fact]
    public async Task AnOfflinePhoneNeverSpendsAChunksAttempts()
    {
        var id = SeedManuscript();
        var offlineCalls = 0;
        claude.Setup(c => c.AnalyzeChapter(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string chapterLabel, IReadOnlyList<string> _, string _, CancellationToken _) =>
                ++offlineCalls <= 20
                    ? Task.FromException<ChapterNotes>(Offline())
                    : Task.FromResult(ChapterNotes.Empty with { Chapter = chapterLabel }));
        var logger = new CapturingLogger<ReviewProcessor>();

        await DriveToCompletion(NewProcessor(logger: logger));

        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == id).Stage);
        Assert.All(context.ChunkAnalyses.Where(a => a.NovelId == id), a => Assert.Equal(0, a.Attempts));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("offline"));
    }

    [Fact]
    public async Task AnOfflinePhoneNeverSpendsAReadsAttempts()
    {
        var id = SeedManuscript();
        FailPass(FirstPass, attempt => attempt <= 12 ? Offline() : null);

        await DriveToCompletion(NewProcessor());

        var novel = LoadNovel(id);
        Assert.Equal(ProjectStage.Completed, novel.Stage);
        Assert.Equal(0, novel.StageAttempts);
    }

    [Fact]
    public async Task AnOfflinePhoneWaitsBeforeAskingAgain_ForTheWholeBook()
    {
        SeedManuscript();
        var calls = new List<int>();
        FailPass(FirstPass, _ => Offline(), calls);
        var processor = NewProcessor();

        var result = await DriveToFirstCall(processor, calls);
        var callsAfterFirst = calls.Count;
        for (var cycle = 0; cycle < 5; cycle++)
            result = await NewProcessor().ProcessNextAsync(CancellationToken.None);

        Assert.Equal(callsAfterFirst, calls.Count);
        Assert.Equal(ProcessOutcome.Waiting, result.Outcome);
        Assert.True(result.Wait > TimeSpan.FromSeconds(30), $"Waited only {result.Wait}");
    }

    [Fact]
    public async Task Review_ProducesReportPdf_AndCompletes()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == id);
        Assert.Equal(ProjectStage.Completed, novel.Stage);

        var analysis = context.BookAnalyses.Single(a => a.NovelId == id);
        Assert.Equal(WorkStatus.Completed, analysis.PdfStatus);
        Assert.NotNull(analysis.PdfPath);
        Assert.True(File.Exists(analysis.PdfPath));

        // The rendered report opens as a valid PDF.
        using var document = PdfReader.Open(analysis.PdfPath!, PdfDocumentOpenMode.ReadOnly);
        Assert.True(document.PageCount > 0);
    }

    /// <summary>
    /// A book soft-deleted mid-review (DESIGN.md §6's undo window) must not keep being worked on —
    /// spending a model call on a book the owner is about to lose for good is billed for nothing.
    /// </summary>
    [Fact]
    public async Task ADeletedBookIsNeverAdvanced()
    {
        var id = SeedManuscript();
        using (var context = database.NewContext())
        {
            var novel = context.Novels.Single(n => n.Id == id);
            novel.DeletedAtUtc = clock.UtcNow;
            context.SaveChanges();
        }

        var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.Idle, result.Outcome);
        using var check = database.NewContext();
        Assert.Equal(ProjectStage.Created, check.Novels.Single(n => n.Id == id).Stage);
    }

    /// <summary>
    /// A book with no jacket gets one painted by the pipeline itself — the gap the first real device
    /// run surfaced: the app only ever painted a cover from a manual Settings action, so a book run
    /// end to end never got one.
    /// </summary>
    [Fact]
    public async Task Review_PaintsAJacket_WhenNoneIsSetOrPainted()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == id);
        Assert.True(novel.CoverAttempted);
        Assert.NotNull(novel.CoverPath);
        Assert.True(File.Exists(novel.CoverPath));
        Assert.False(novel.CoverIsOwners);
    }

    /// <summary>
    /// A jacket Gemini refuses is a smaller loss than a report that never completes: the review still
    /// reaches Completed, CoverAttempted settles so the worker does not retry a moment the model has
    /// already declined, and CoverPath stays null so the render falls back to the page's own ink.
    /// </summary>
    [Fact]
    public async Task Review_StillCompletes_WhenTheJacketIsRefused()
    {
        gemini.Setup(g => g.GenerateJacket(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ImageContentBlockedException("refused", ImageBlockReason.Safety));

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == id);
        Assert.Equal(ProjectStage.Completed, novel.Stage);
        Assert.True(novel.CoverAttempted);
        Assert.Null(novel.CoverPath);

        var analysis = context.BookAnalyses.Single(a => a.NovelId == id);
        Assert.Equal(WorkStatus.Completed, analysis.PdfStatus);
    }

    [Fact]
    public async Task RateLimitedChapterAnalysis_RetriesAndSucceeds()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        // The first AnalyzeChapter threw a rate limit; the pipeline must have rescheduled and retried.
        Assert.True(chapterAnalysisCalls >= 2);

        using var context = database.NewContext();
        Assert.All(
            context.ChunkAnalyses.Where(a => a.NovelId == id).ToList(),
            a => Assert.Equal(WorkStatus.Completed, a.Status));
    }

    [Fact]
    public async Task Scenes_AreDetected_AndGetEvidenceImages()
    {
        var id = SeedManuscript("The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.");
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var scenes = context.Scenes.Where(s => s.NovelId == id).OrderBy(s => s.Number).ToList();
        Assert.True(scenes.Count >= 2, "expected the divider to split the manuscript into scenes");
        Assert.All(scenes, s => Assert.Equal(WorkStatus.Completed, s.ImageStatus));
        Assert.All(scenes, s =>
        {
            Assert.NotNull(s.ImagePath);
            Assert.True(File.Exists(s.ImagePath));
        });
    }

    // Seeds a follow-on draft of an existing project the way ProjectService.CreateNextDraftAsync does:
    // same ProjectId, the next DraftNumber, and PreviousDraftId pointing at the draft it was made from.
    private Guid SeedNextDraft(Guid previousId, string text)
    {
        using var context = database.NewContext();
        var previous = context.Novels.Single(n => n.Id == previousId);
        var draft = new Novel
        {
            Title = previous.Title, GoogleDocId = "doc",
            ProjectId = previous.ProjectId, DraftNumber = previous.DraftNumber + 1, PreviousDraftId = previous.Id,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        draft.Source = new SourceText { NovelId = draft.Id, FullText = text, CharacterCount = text.Length };
        context.Novels.Add(draft);
        context.SaveChanges();
        return draft.Id;
    }

    [Fact]
    public async Task SecondDraft_UnchangedText_ReusesEverySceneImage()
    {
        const string text = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        var firstId = SeedManuscript(text);
        await DriveToCompletion(NewProcessor());

        // The second draft re-picks the identical document — every scene should reuse the prior render.
        gemini.Invocations.Clear();
        var secondId = SeedNextDraft(firstId, text);
        await DriveToCompletion(NewProcessor());

        gemini.Verify(g => g.GenerateSceneImage(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
            It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Never());

        using var context = database.NewContext();
        var scenes = context.Scenes.Where(s => s.NovelId == secondId).OrderBy(s => s.Number).ToList();
        Assert.True(scenes.Count >= 2);
        Assert.All(scenes, s =>
        {
            Assert.Equal(WorkStatus.Completed, s.ImageStatus);
            Assert.False(s.Changed);
            Assert.NotNull(s.PreviousSceneId);
            Assert.NotNull(s.ImagePath);
            Assert.True(File.Exists(s.ImagePath));
        });
    }

    [Fact]
    public async Task SecondDraft_RerendersOnlyTheChangedScene()
    {
        const string first = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        // Scene 1 is byte-identical; scene 2 is reworded but shares most content words (a changed match).
        const string second = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked loudly and waited in the cold.";
        var firstId = SeedManuscript(first);
        await DriveToCompletion(NewProcessor());

        gemini.Invocations.Clear();
        var secondId = SeedNextDraft(firstId, second);
        await DriveToCompletion(NewProcessor());

        // Exactly one fresh render — the changed scene. The unchanged scene reused its prior image.
        gemini.Verify(g => g.GenerateSceneImage(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
            It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Once());

        using var context = database.NewContext();
        var scenes = context.Scenes.Where(s => s.NovelId == secondId).OrderBy(s => s.Number).ToList();
        var changed = scenes.Where(s => s.Changed).ToList();
        Assert.Single(changed);
        Assert.All(scenes, s => Assert.NotNull(s.PreviousSceneId));   // both matched a prior scene
        Assert.All(scenes, s => Assert.Equal(WorkStatus.Completed, s.ImageStatus));
    }

    /// <summary>
    /// Release G end to end: the draft's own text reaches the report and the findings are marked on
    /// it.
    ///
    /// <para>Written because the suite stayed green with the manuscript threaded through every
    /// signature and never actually read — the same silent shape as the pair that was wired
    /// everywhere but loaded nowhere. Only the composed PDF can tell the difference, so this reads
    /// the prose back out of it.</para>
    /// </summary>
    [Fact]
    public async Task TheManuscriptIsMarkedUpInsideTheComposedReport()
    {
        const string text = "He felt the cold. The knight rode to the castle and knocked on the gate.";
        var id = SeedManuscript(text);

        await DriveToCompletion(NewProcessor());

        Assert.Contains("knocked on the gate", TextOf(ReportOf(id)), StringComparison.Ordinal);
    }

    /// <summary>
    /// A draft every read was silent on still prints its manuscript, because each scene's block is
    /// inserted into it. A hundred and forty pages of unmarked prose is a copy of the book — but the
    /// scene blocks make it a reading. Only a draft with no marks <i>and</i> no scenes prints no
    /// manuscript; that case is <c>SceneBlockTests.WithNoMarksAndNoScenesThereIsNoManuscript</c>.
    /// </summary>
    [Fact]
    public async Task ADraftEveryReadWasSilentOnStillPrintsItsManuscriptForTheSceneBlocks()
    {
        claude.Setup(c => c.ConsolidateAnalysis(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookAnalysisResult.Empty);
        claude.Setup(c => c.ConsolidateCraft(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CraftReport([], [], [], []));
        claude.Setup(c => c.ProposeExpansion(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExpansionReport.Empty);
        claude.Setup(c => c.ReadSection(It.IsAny<Persona>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, string _, CancellationToken _) => Task.FromResult(SectionRead.Silent(persona.Id)));

        var id = SeedManuscript("The knight rode to the castle and knocked on the gate.");
        await DriveToCompletion(NewProcessor());

        Assert.Contains("knocked on the gate", TextOf(ReportOf(id)), StringComparison.Ordinal);
    }

    /// <summary>
    /// A finding that cites only a chapter is written on that chapter's opening, so the draft prints.
    /// The default consolidation cites "Chapter 1" with no quotation.
    /// </summary>
    [Fact]
    public async Task AFindingThatCitesOnlyAChapterStillPrintsTheManuscriptWithItMarked()
    {
        var id = SeedManuscript("The knight rode to the castle and knocked on the gate.");
        await DriveToCompletion(NewProcessor());

        Assert.Contains("knocked on the gate", TextOf(ReportOf(id)), StringComparison.Ordinal);
    }

    private static string TextOf(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(' ', document.GetPages().Select(page => page.Text));
    }

    /// <summary>
    /// F41 end to end: the changed scene's predecessor reaches the composed report, so the block
    /// prints a before and an after.
    ///
    /// <para>This exists because the suite went green with the pair wired everywhere except the one
    /// step that loads it — the record had the field, the renderer could draw two panels, and
    /// nothing put an image in it. That is the same silent gap a stubbed call left in Release F, and
    /// a rendered report is the only thing that can tell the difference.</para>
    /// </summary>
    [Fact]
    public async Task AChangedScenesPredecessorReachesTheComposedReport()
    {
        const string first = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        const string second = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked loudly and waited in the cold.";
        var firstId = SeedManuscript(first);
        await DriveToCompletion(NewProcessor());
        var secondId = SeedNextDraft(firstId, second);
        await DriveToCompletion(NewProcessor());

        Assert.True(Pictures(ReportOf(secondId)) > Pictures(ReportOf(firstId)),
            "The second draft's report carries no more pictures than the first's, though one of its "
            + "two scenes changed and should print its predecessor beside it.");
    }

    private static int Pictures(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return document.GetPages().Sum(page => page.GetImages().Count());
    }

    private byte[] ReportOf(Guid novelId)
    {
        using var context = database.NewContext();
        var path = context.BookAnalyses.Single(a => a.NovelId == novelId).PdfPath;
        Assert.NotNull(path);
        return File.ReadAllBytes(path!);
    }

    [Fact]
    public async Task Review_DeliversToRemarkable_AtTheRoot_WhenEnabledAndPaired()
    {
        var remarkable = new Mock<IRemarkableClient>();

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));

        // Delivery rides the proven root path Foreword uses (the "Canonizer" folder path relied on a
        // reMarkable wire format never verified against the live cloud, and silently delivered nothing).
        // Named through the same function the Drive upload uses, so the two targets cannot come to
        // different names for one document — which is what a hand-written "Analysis" here let happen.
        remarkable.Verify(
            r => r.Upload(DeliveredDocument.Name("Quest", 1), It.IsAny<byte[]>()), Times.Once);
        remarkable.Verify(r => r.EnsureFolder(It.IsAny<string>()), Times.Never);

        using var context = database.NewContext();
        Assert.True(context.BookAnalyses.Single(a => a.NovelId == id).DeliveredToRemarkable);
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == id).Stage);
    }

    [Fact]
    public async Task Review_DoesNotDeliverToRemarkable_WhenDisabled()
    {
        var remarkable = new Mock<IRemarkableClient>();
        SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = false }, remarkable.Object));

        remarkable.Verify(r => r.Upload(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task Review_StillCompletes_WhenRemarkableDeliveryKeepsFailing()
    {
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ThrowsAsync(new HttpRequestException("reMarkable is down"));

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));

        using var context = database.NewContext();
        // Delivery gave up after its budget, but the review still reached Completed (the report is on disk).
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == id).Stage);
        Assert.True(context.BookAnalyses.Single(a => a.NovelId == id).DeliveredToRemarkable);   // settled
    }

    [Fact]
    public async Task Review_PersistsTheExpansionRead_AndFoldsItIntoTheReport()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using (var context = database.NewContext())
        {
            var analysis = context.BookAnalyses.Single(a => a.NovelId == id);
            Assert.False(string.IsNullOrEmpty(analysis.ExpansionJson));   // the additive step ran and persisted
            Assert.Equal(WorkStatus.Completed, analysis.PdfStatus);       // the folded report still composed
        }

        // The in-app read path folds the expansion in too.
        var projects = new ProjectService(database, new Mock<IGoogleDocsClient>().Object,
            new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
            clock, Options.Create(new RateLimitOptions()));
        var report = await projects.GetReportAsync(id, CancellationToken.None);
        Assert.NotNull(report);
        Assert.NotEmpty(report!.Expansion.ScenesToAdd);
        Assert.False(string.IsNullOrWhiteSpace(report.Expansion.Length.Note));
    }

    [Fact]
    public async Task Map_ThreadsRunningStoryStateIntoContinuitySummary()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == id);
        // The map step carried the model's running state onto the manuscript for the consolidation to use.
        Assert.False(string.IsNullOrWhiteSpace(novel.ContinuitySummary));
        Assert.Contains("the hero reached the castle", novel.ContinuitySummary);
    }

    [Fact]
    public async Task FirstDraft_PersistsFindingsAsRows_AllNew()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var findings = context.Findings.Where(f => f.NovelId == id).ToList();
        // The faked story-half returns one finding in each of the four categories.
        Assert.Equal(4, findings.Count);
        Assert.All(findings, f =>
        {
            Assert.Equal(FindingStatus.New, f.Status);
            Assert.Equal(1, f.FirstDraftNumber);
            Assert.NotEqual(Guid.Empty, f.ThreadId);
        });
        Assert.Contains(findings, f => f.Category == FindingCategory.PlotHole);
        Assert.Contains(findings, f => f.Category == FindingCategory.Pacing);
    }

    [Fact]
    public async Task SecondDraft_CarriesAMatchedFindingByThreadId()
    {
        var firstId = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        // The plot-hole thread from draft 1 (candidate index 0 in the reconciliation order).
        Guid plotHoleThread;
        using (var context = database.NewContext())
            plotHoleThread = context.Findings.Single(f => f.NovelId == firstId && f.Category == FindingCategory.PlotHole).ThreadId;

        // For draft 2, tell the reconciliation the plot hole is the same issue as draft 1's; the rest are new.
        claude.Setup(c => c.ReconcileFindings(
                It.IsAny<IReadOnlyList<PriorFinding>>(), It.IsAny<IReadOnlyList<CandidateFinding>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<PriorFinding> prior, IReadOnlyList<CandidateFinding> candidates, CancellationToken _) =>
                Task.FromResult(new FindingReconciliation(
                    candidates.Select(c => new FindingMatch(c.Index, c.Index == 0 ? plotHoleThread : (Guid?)null)).ToList())));

        var secondId = SeedNextDraft(firstId, "A short tale. The hero walked to the castle and knocked on the gate.");
        await DriveToCompletion(NewProcessor());

        using var check = database.NewContext();
        var carried = check.Findings.Single(f => f.NovelId == secondId && f.Category == FindingCategory.PlotHole);
        Assert.Equal(plotHoleThread, carried.ThreadId);   // same identity across drafts
        Assert.Equal(FindingStatus.Carried, carried.Status);
        Assert.Equal(1, carried.FirstDraftNumber);          // open since draft 1
        // The other three findings had no match, so they are new threads this draft.
        Assert.Equal(3, check.Findings.Count(f => f.NovelId == secondId && f.Status == FindingStatus.New));
    }

    // Seeds book N+1 of a series the way ProjectService.CreateNextBookAsync does: a NEW project (its own
    // draft chain) linked to the prior book via PreviousBookId, so the pipeline can reuse the prior book's canon.
    private Guid SeedNextBook(Guid previousBookId, string text)
    {
        using var context = database.NewContext();
        var previous = context.Novels.Single(n => n.Id == previousBookId);
        var book = new Novel
        {
            Title = previous.Title, GoogleDocId = "doc-b2",
            SeriesId = previous.SeriesId ?? previous.ProjectId,
            PreviousBookId = previous.Id, SeriesSequence = 2,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        book.ProjectId = book.Id;
        book.Source = new SourceText { NovelId = book.Id, FullText = text, CharacterCount = text.Length };
        context.Novels.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    [Fact]
    public async Task SecondBook_ReusesReferenceImages_ForReturningEntities()
    {
        var firstId = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using (var ctx = database.NewContext())
            Assert.NotEmpty(ctx.DesignRefs.Where(r => r.NovelId == firstId && r.ReferenceImagePath != null).ToList());

        // Book two re-uses the same cast (Hero + Castle) as book one — both should reuse their prior sheets.
        gemini.Invocations.Clear();
        var secondId = SeedNextBook(firstId, "A short tale. The hero walked to the castle and knocked on the gate.");
        await DriveToCompletion(NewProcessor());

        gemini.Verify(g => g.GenerateReferenceSheet(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());

        using var check = database.NewContext();
        var refs = check.DesignRefs.Where(r => r.NovelId == secondId && r.NeedsSheet).ToList();
        Assert.NotEmpty(refs);
        Assert.All(refs, r =>
        {
            Assert.Equal(WorkStatus.Completed, r.ReferenceSheetStatus);
            Assert.NotNull(r.ReferenceImagePath);
            Assert.True(File.Exists(r.ReferenceImagePath));
        });
    }

    [Fact]
    public async Task SecondBook_RendersAFreshSheet_ForANewEntity()
    {
        var firstId = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        // Book two keeps the Hero (returning) but introduces a Villain the prior book never had.
        claude.Setup(c => c.ExtractEntities(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesignBibleFragment(
                [new EntityMention("Hero", "a brave knight in silver armor"),
                 new EntityMention("Villain", "a cloaked sorcerer")],
                [new EntityMention("Castle", "a grey stone fortress")]));

        gemini.Invocations.Clear();
        var secondId = SeedNextBook(firstId, "The hero met the villain at the castle.");
        await DriveToCompletion(NewProcessor());

        // Exactly one fresh sheet — the new Villain; Hero and Castle reused their prior images.
        gemini.Verify(g => g.GenerateReferenceSheet(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());

        using var check = database.NewContext();
        var refs = check.DesignRefs.Where(r => r.NovelId == secondId && r.NeedsSheet).ToList();
        Assert.All(refs, r =>
        {
            Assert.Equal(WorkStatus.Completed, r.ReferenceSheetStatus);
            Assert.True(File.Exists(r.ReferenceImagePath!));
        });
    }

    [Fact]
    public async Task StandaloneBook_SkipsTheSeriesContinuityRead()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        claude.Verify(c => c.AssessSeriesContinuity(
            It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<IReadOnlyList<ChapterNotes>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        using var context = database.NewContext();
        Assert.Null(context.BookAnalyses.Single(a => a.NovelId == id).SeriesContinuityJson);
    }

    [Fact]
    public async Task SecondBook_RunsTheSeriesContinuityRead_AndFoldsItIntoTheReport()
    {
        var firstId = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        // The sequel finds a contradiction of book one's canon and leaves one of its threads open.
        claude.Setup(c => c.AssessSeriesContinuity(
                It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<IReadOnlyList<ChapterNotes>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, SeriesCanon canon, IReadOnlyList<ChapterNotes> _, string _, CancellationToken _) =>
                Task.FromResult(new SeriesContinuity(
                    canon.PriorBookTitle,
                    [new SeriesBreak("Chapter 1", "The hero's eyes were grey", "Now they are green")],
                    [new CarriedThread("The gate's mechanism was never explained", "Ignored")])));

        var secondId = SeedNextBook(firstId, "A short tale. The hero walked to the castle and knocked on the gate.");
        await DriveToCompletion(NewProcessor());

        claude.Verify(c => c.AssessSeriesContinuity(
            It.IsAny<string>(), It.Is<SeriesCanon>(canon => canon.OpenThreads.Count > 0),
            It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        using (var context = database.NewContext())
        {
            var analysis = context.BookAnalyses.Single(a => a.NovelId == secondId);
            Assert.False(string.IsNullOrEmpty(analysis.SeriesContinuityJson));   // persisted
            Assert.Equal(WorkStatus.Completed, analysis.PdfStatus);              // the folded report still composed
            Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == secondId).Stage);
        }

        // The in-app read path folds the series continuity in too.
        var projects = new ProjectService(database, new Mock<IGoogleDocsClient>().Object,
            new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
            clock, Options.Create(new RateLimitOptions()));
        var report = await projects.GetReportAsync(secondId, CancellationToken.None);
        Assert.NotNull(report);
        Assert.True(report!.SeriesContinuity.HasContent);
        Assert.Single(report.SeriesContinuity.Breaks);
        Assert.Single(report.SeriesContinuity.CarriedThreads);
    }

    [Fact]
    public async Task SecondBook_StillCompletes_WhenTheContinuityReadKeepsFailing()
    {
        var firstId = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        claude.Setup(c => c.AssessSeriesContinuity(
                It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<IReadOnlyList<ChapterNotes>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Claude is down"));

        var secondId = SeedNextBook(firstId, "A short tale. The hero walked to the castle and knocked on the gate.");

        // The report never reaches Completed while the continuity call keeps failing, but it must not crash the
        // worker — it stays parked with a recorded error, resumable when the call recovers. Drive a bounded run.
        var processor = NewProcessor();
        for (var cycle = 0; cycle < 40; cycle++)
        {
            var result = await processor.ProcessNextAsync(CancellationToken.None);
            if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        }

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == secondId);
        Assert.NotEqual(ProjectStage.Completed, novel.Stage);   // parked on the failing continuity call
        Assert.NotNull(novel.LastError);
    }

    [Fact]
    public async Task DesignBible_RendersSheetsForRecurringEntities()
    {
        // Two chunks so an entity seen in both is "recurring" and earns a reference sheet.
        var id = SeedManuscript(string.Join("\n\n", Enumerable.Repeat("The Hero rode toward the Castle.", 4000)));
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var sheets = context.DesignRefs.Where(r => r.NovelId == id && r.NeedsSheet).ToList();
        Assert.NotEmpty(sheets);
        Assert.All(sheets, r => Assert.Equal(WorkStatus.Completed, r.ReferenceSheetStatus));
    }
}
