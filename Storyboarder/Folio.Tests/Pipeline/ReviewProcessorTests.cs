using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PdfSharp.Pdf.IO;
using Folio.Domain.Analysis;
using Folio.Domain.Reading;
using Folio.Domain.Scenes;
using Folio.Domain.Series;
using Folio.Domain.Text;
using Folio.Domain.External;
using Folio.Domain.Imaging;
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
using Storyworthtelling.Pipeline;

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
    private readonly List<string> biblesSeen = [];
    private int hostCalls;
    private int speakCalls;
    private IReadOnlyList<Review> roundTablePanel = [];
    private readonly List<IReadOnlyList<string>> placementBooks = [];
    private readonly List<string> sectionReads = [];
    private readonly List<string> readerReads = [];
    private readonly List<GenreFrame> readerGenres = [];
    private readonly List<ReadingFrame> readerFrames = [];
    private readonly List<SceneToJudge> judgedScenes = [];
    private readonly List<string> synthesisReads = [];
    private int sectionsSeenBySynthesis;
    private IReadOnlyList<Folio.Domain.Markup.FindingEntry> findingsSeenByPlan = [];
    private IReadOnlyList<RevisionItem> previousPlanSeen = [];
    private int readersSeenBySynthesis;

    public ReviewProcessorTests()
    {
        claude.Setup(c => c.ExtractEntities(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesignBibleFragment(
                [new EntityMention("Hero", "a brave knight in silver armor")],
                [new EntityMention("Castle", "a grey stone fortress")]));

        claude.Setup(c => c.DeriveArtStyle(It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ink-wash watercolor, muted palette");

        // The sixteen passes. Stubbed for real rather than left to Moq's defaults: a default-valued
        // Task<T> completes with null, which the pipeline records as a finished pass — so the step
        // would appear to work while reading nothing, which is the failure PassPipelineTests names.
        claude.Setup(c => c.ReadSection(It.IsAny<Persona>(), It.IsAny<string>(), It.IsAny<ReadingFrame>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, string _, ReadingFrame _, CancellationToken _) =>
            {
                sectionReads.Add(persona.Id);
                return Task.FromResult(new SectionRead(
                    persona.Id, $"{persona.Name}: one thing to report.",
                    [new ReportFinding("Chapter 1", $"{persona.Name} found something.")]));
            });

        claude.Setup(c => c.ReadAsReader(It.IsAny<Persona>(), It.IsAny<string>(), It.IsAny<ReadingFrame>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, string _, ReadingFrame frame, CancellationToken _) =>
            {
                readerReads.Add(persona.Id);
                readerGenres.Add(frame.Genre);
                readerFrames.Add(frame);
                return Task.FromResult(new Review(
                    persona.Id, "A verdict in one clause.", "A sustained read in character.",
                    ["what works"], ["what does not"], "what it would need")
                { Score = 7, ScoreReason = "One clause on why." });
            });

        claude.Setup(c => c.Synthesise(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<SectionRead>>(), It.IsAny<IReadOnlyList<Review>>(),
                It.IsAny<int>(), It.IsAny<IReadOnlyList<Folio.Domain.Markup.FindingEntry>>(),
                It.IsAny<IReadOnlyList<RevisionItem>>(), It.IsAny<CancellationToken>()))
            .Returns((string passId, IReadOnlyList<SectionRead> sections, IReadOnlyList<Review> readers, int _,
                IReadOnlyList<Folio.Domain.Markup.FindingEntry> findings, IReadOnlyList<RevisionItem> previousPlan,
                CancellationToken _) =>
            {
                synthesisReads.Add(passId);
                findingsSeenByPlan = findings;
                previousPlanSeen = previousPlan;
                sectionsSeenBySynthesis = sections.Count;
                readersSeenBySynthesis = readers.Count;
                return Task.FromResult(
                    new SynthesisRead([new RevisionItem("Must", "Ch. 12", "The middle", "Compress it.")], Verdict.Empty));
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
                [], [], [], [])
            { Genre = "Upmarket fantasy", WordCountRange = "90,000-120,000" });

        claude.Setup(c => c.ConsolidateCraft(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CraftReport(
                [new LineEdit("Chapter 1", "He felt the cold.", "The cold came.", "Filtered.")],
                [new ProseNote("Chapter 1", "Filter words", "\"he felt\"", "Cut the filter.")],
                [new DialogueNote("Chapter 1", "Hero", "On the nose.", "Trust the subtext.")],
                [new SceneBeat("Chapter 1", "The hero knocks.", "Opens the confrontation.", "Earns its place.")]));

        // Each detected scene is judged on its own; the judgement names the scene it was asked about, so a
        // verdict printed under the wrong scene shows up in the report text.
        claude.Setup(c => c.JudgeScene(It.IsAny<SceneToJudge>(), It.IsAny<DesignBible>(), It.IsAny<CancellationToken>()))
            .Returns((SceneToJudge scene, DesignBible _, CancellationToken _) =>
            {
                judgedScenes.Add(scene);
                return Task.FromResult(new SceneJudgement(
                    scene.Number, scene.Chapter, $"Scene {scene.Number} does its own job.", SceneCall.Keep, 0.5,
                    $"Verdict for scene {scene.Number}.", []));
            });

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
                It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<string>(), It.IsAny<StoryPlacement?>(), It.IsAny<IReadOnlyList<ChapterNotes>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, SeriesCanon canon, string _, StoryPlacement? _, IReadOnlyList<ChapterNotes> _, string _, CancellationToken _) =>
                Task.FromResult(SeriesContinuity.Empty with { PriorBookTitle = canon.PriorBookTitle }));

        // Default series bible read (release G): the book establishes one character.
        claude.Setup(c => c.ReadSeriesBible(
                It.IsAny<string>(), It.IsAny<ReadingFrame>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, ReadingFrame _, string _, string soFar, CancellationToken _) =>
            {
                biblesSeen.Add(soFar);
                return Task.FromResult<IReadOnlyList<BibleFact>>(
                    [new BibleFact(BibleKind.Character, "Hero", [], "", "Now a knight errant.", "The knight's son rode out")]);
            });

        // Default round table (release H): the host hands to Hugo Hook once, then closes.
        claude.Setup(c => c.RoundTableHost(
                It.IsAny<IReadOnlyList<Review>>(), It.IsAny<RoundTable>(), It.IsAny<IReadOnlyList<RoundTableCue>>(),
                It.IsAny<int>(), It.IsAny<StoryLength>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<Review> reviews, RoundTable _, IReadOnlyList<RoundTableCue> _, int _, StoryLength _, CancellationToken _) =>
            {
                hostCalls++;
                roundTablePanel = reviews;
                return Task.FromResult(hostCalls == 1
                    ? new HostMove("Welcome to the round table.", "agent", "Hugo, what sold you?", "", false)
                    : new HostMove("That's the show.", "", "", "", true));
            });
        claude.Setup(c => c.RoundTableSpeak(
                It.IsAny<Persona>(), It.IsAny<IReadOnlyList<Review>>(), It.IsAny<RoundTable>(), It.IsAny<RoundTableCue>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, IReadOnlyList<Review> _, RoundTable _, RoundTableCue _, CancellationToken _) =>
            {
                speakCalls++;
                return Task.FromResult($"{persona.ReaderName} says the gate scene sold them.");
            });
        claude.Setup(c => c.RoundTableClose(It.IsAny<IReadOnlyList<Review>>(), It.IsAny<RoundTable>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoundTableClosing("Goodnight, everyone.", new RoundTableSummary(["The gate scene works"], ["Whether it sells"])));

        // Default placement guess (release G, R21): after the latest-written other book.
        claude.Setup(c => c.PlaceInSeries(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> books, string _, string _, CancellationToken _) =>
            {
                placementBooks.Add(books);
                return Task.FromResult(StoryPlacement.After(books[^1]));
            });

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
        IPaperDriveArchive? driveArchive = null, ILogger<ReviewProcessor>? logger = null, IImageStore? store = null,
        Storyworthtelling.Voice.IVoiceClient? voiceClient = null, Storyworthtelling.Voice.IVoiceProfileStore? voiceStore = null) => new(
        database,
        claude.Object,
        gemini.Object,
        store ?? new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
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
            : new PaperDelivery(remarkable, driveArchive),
        voiceClient: voiceClient,
        voiceStore: voiceStore);

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
    /// End to end: all sixteen passes run, each once, in plan order — and the synthesis passes see
    /// the other fourteen, which is the only thing that makes them synthesis rather than a
    /// fifteenth opinion.
    /// </summary>
    private sealed class HeldProfile : Storyworthtelling.Voice.IVoiceProfileStore
    {
        public Storyworthtelling.Voice.VoiceProfile Profile { get; set; } = Storyworthtelling.Voice.VoiceProfile.Empty;

        public int Saves { get; private set; }

        public Task<Storyworthtelling.Voice.VoiceProfile> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Profile);

        public Task SaveAsync(Storyworthtelling.Voice.VoiceProfile profile, CancellationToken cancellationToken)
        {
            Profile = profile;
            Saves++;
            return Task.CompletedTask;
        }
    }

    private static Mock<Storyworthtelling.Voice.IVoiceClient> RefiningVoice(List<(string Text, Guid Project)> refined)
    {
        var voice = new Mock<Storyworthtelling.Voice.IVoiceClient>();
        voice.Setup(client => client.RefineAsync(
                It.IsAny<Storyworthtelling.Voice.VoiceProfile>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Storyworthtelling.Voice.VoiceProfile existing, string text, Guid project, CancellationToken _) =>
            {
                refined.Add((text, project));
                return existing with { Register = "plain", DocumentsAnalyzed = existing.DocumentsAnalyzed + 1, NeedsRelearn = false };
            });
        return voice;
    }

    /// <summary>
    /// The voice is learned from a sample spread across the whole manuscript, not its opening (audit W10.6).
    /// </summary>
    [Fact]
    public async Task TheVoiceIsLearnedFromTheWholeBooksSample()
    {
        var text = string.Join(" ", Enumerable.Range(1, 6000).Select(n => $"word{n}")) + " The hero knocked on the gate.";
        SeedManuscript(text);
        var refined = new List<(string Text, Guid Project)>();

        await DriveToCompletion(NewProcessor(voiceClient: RefiningVoice(refined).Object, voiceStore: new HeldProfile()));

        Assert.Equal(Storyworthtelling.Voice.VoiceSample.Of(text), Assert.Single(refined).Text);
    }

    /// <summary>
    /// A profile marked to be learned again (a discarded book, or an older shape) is rebuilt from every book that
    /// remains at the next ingest, from nothing (audit W10.6), and no book is then refined a second time.
    /// </summary>
    [Fact]
    public async Task AProfileMarkedToRelearnIsRebuiltFromEveryBookThatRemains()
    {
        var first = SeedManuscript("The first book. The hero walked to the castle and knocked on the gate.");
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = SeedManuscript("The second book. The heroine rode to the tower and called up.");
        var store = new HeldProfile
        {
            Profile = Storyworthtelling.Voice.VoiceProfile.Empty with
            {
                Register = "stale", DocumentsAnalyzed = 3, NeedsRelearn = true,
            },
        };
        var refined = new List<(string Text, Guid Project)>();

        await DriveToCompletion(NewProcessor(voiceClient: RefiningVoice(refined).Object, voiceStore: store));

        Assert.Equal([first, second], refined.Select(refine => refine.Project));
        Assert.Equal(2, store.Profile.DocumentsAnalyzed);
        Assert.False(store.Profile.NeedsRelearn);
        using var context = database.NewContext();
        Assert.All(context.Novels, novel => Assert.True(novel.VoiceAnalyzed));
    }

    /// <summary>
    /// Every reader is told the genre the consolidation read out of the text, and the length its
    /// market expects — the frame a length judgement, and the genre fan, both need.
    /// </summary>
    [Fact]
    public async Task EveryReaderIsToldTheGenreReadFromTheText()
    {
        SeedManuscript();
        await DriveToCompletion(NewProcessor());

        Assert.Equal(PersonaCatalogue.Readers.Count, readerGenres.Count);
        Assert.All(readerGenres, genre =>
            Assert.Equal(new GenreFrame("Upmarket fantasy", "90,000-120,000"), genre));
    }

    /// <summary>
    /// The pairing bug (release A3). Claude's own scene list (one beat here) was zipped against the
    /// detected scenes (two here) by position, so the second scene printed nothing and any mismatch
    /// shifted every later verdict. Now every detected scene is judged on its own, and each block prints
    /// its own scene's verdict.
    /// </summary>
    [Fact]
    public async Task EachSceneIsJudgedOnItsOwnAndItsBlockCarriesItsOwnVerdict()
    {
        const string text = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        var id = SeedManuscript(text);
        await DriveToCompletion(NewProcessor());

        Assert.Equal([1, 2], judgedScenes.Select(scene => scene.Number).Order());
        Assert.Contains(judgedScenes, scene => scene.Text.Contains("At the gate", StringComparison.Ordinal));

        var report = ReportText(id);
        Assert.Contains("Scene 1 does its own job.", report, StringComparison.Ordinal);
        Assert.Contains("Scene 2 does its own job.", report, StringComparison.Ordinal);
        Assert.True(
            report.IndexOf("At the gate", StringComparison.Ordinal) < report.IndexOf("Scene 2 does its own job.", StringComparison.Ordinal),
            "Scene two's verdict should follow scene two's prose.");
        Assert.DoesNotContain("Opens the confrontation.", report, StringComparison.Ordinal);
    }

    // ---- Batches (owner's ruling, R39): the scene calls go to the batch tier at half the cost ---------

    private const string TwoScenes = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";

    /// <summary>
    /// The scene judgements go in one batch; while it runs, the pipeline polls and carries on; when it
    /// ends, each judgement lands on its own scene (by key, never by position), and no scene is judged
    /// one at a time.
    /// </summary>
    [Fact]
    public async Task SceneJudgementsAreBatchedAndLandOnTheirOwnScenes()
    {
        var polls = 0;
        claude.Setup(c => c.SubmitSceneJudgements(It.IsAny<IReadOnlyList<SceneToJudge>>(), It.IsAny<DesignBible>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("batch-judgements");
        claude.Setup(c => c.BatchEnded("batch-judgements", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++polls >= 2);
        claude.Setup(c => c.SceneJudgementResults("batch-judgements", It.IsAny<IReadOnlyDictionary<int, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyDictionary<int, string> chapters, CancellationToken _) =>
                chapters.Keys.Reverse().Select(number => new BatchItem<SceneJudgement>(number,
                    new SceneJudgement(number, chapters[number], $"Batched scene {number} does its job.", SceneCall.Keep, 0.5, "v", []), null)).ToList());

        var id = SeedManuscript(TwoScenes);
        await DriveToCompletion(NewProcessor());

        Assert.Empty(judgedScenes);
        Assert.True(polls >= 2, "The batch should have been polled until it ended.");
        var report = ReportText(id);
        Assert.True(report.IndexOf("At the gate", StringComparison.Ordinal) < report.IndexOf("Batched scene 2 does its job.", StringComparison.Ordinal));
        Assert.True(report.IndexOf("Batched scene 1 does its job.", StringComparison.Ordinal) < report.IndexOf("At the gate", StringComparison.Ordinal));
    }

    /// <summary>
    /// The reads (R39): for each model, one pass runs on its own to write the manuscript to the cache, then
    /// the rest of that model's passes go in one batch. A pass the batch did not complete runs one at a time.
    /// </summary>
    [Fact]
    public async Task ReadsWarmTheCacheOnceThenGoInOneBatchPerModel()
    {
        var submitted = new List<string[]>();
        claude.Setup(c => c.SubmitReads(It.IsAny<IReadOnlyList<Persona>>(), It.IsAny<string>(), It.IsAny<ReadingFrame>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Persona> personas, string _, ReadingFrame _, CancellationToken _) =>
            {
                submitted.Add([.. personas.Select(persona => persona.Id)]);
                return $"batch-{submitted.Count}";
            });
        claude.Setup(c => c.BatchEnded(It.Is<string>(id => id.StartsWith("batch-")), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        claude.Setup(c => c.ReadResults(It.IsAny<string>(), It.IsAny<IReadOnlyList<Persona>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<Persona> personas, CancellationToken _) =>
                personas.Select(persona => persona.Id == "harsh-critic"
                    ? new PassBatchItem(persona.Id, null, "errored: overloaded")
                    : new PassBatchItem(persona.Id, persona.Kind == PersonaKind.Reader
                        ? JsonSerializer.Serialize(new Review(persona.Id, "v", "a", ["s"], ["w"], "c") { Score = 6, ScoreReason = "r" })
                        : JsonSerializer.Serialize(new SectionRead(persona.Id, "s", [])), null)).ToList());

        SeedManuscript();
        await DriveToCompletion(NewProcessor());

        // One direct pass per model warms the cache, then one batch per model.
        Assert.Equal([PersonaCatalogue.Sections[0].Id], sectionReads);
        Assert.Equal([PersonaCatalogue.Readers[0].Id, "harsh-critic"], readerReads);
        Assert.Equal(2, submitted.Count);
        Assert.Equal(PersonaCatalogue.Sections.Skip(1).Select(persona => persona.Id), submitted[0]);
        Assert.Equal(PersonaCatalogue.Readers.Skip(1).Select(persona => persona.Id), submitted[1]);
    }

    /// <summary>A scene the batch could not judge goes one at a time afterwards; the batch is never resubmitted.</summary>
    [Fact]
    public async Task ASceneTheBatchMissedIsJudgedOneAtATime()
    {
        var submits = 0;
        claude.Setup(c => c.SubmitSceneJudgements(It.IsAny<IReadOnlyList<SceneToJudge>>(), It.IsAny<DesignBible>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { submits++; return "batch-judgements"; });
        claude.Setup(c => c.BatchEnded("batch-judgements", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        claude.Setup(c => c.SceneJudgementResults("batch-judgements", It.IsAny<IReadOnlyDictionary<int, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyDictionary<int, string> chapters, CancellationToken _) =>
            [
                new BatchItem<SceneJudgement>(1, new SceneJudgement(1, chapters[1], "Batched.", SceneCall.Keep, 0.5, "v", []), null),
                new BatchItem<SceneJudgement>(2, null, "errored: overloaded"),
            ]);

        SeedManuscript(TwoScenes);
        await DriveToCompletion(NewProcessor());

        Assert.Equal(1, submits);
        Assert.Equal([2], judgedScenes.Select(scene => scene.Number));
    }

    /// <summary>The image prompts go in one batch too; each prompt lands on its own scene and drives its render.</summary>
    [Fact]
    public async Task ImagePromptsAreBatchedAndRenderTheirOwnScenes()
    {
        claude.Setup(c => c.SubmitSceneImagePrompts(It.IsAny<IReadOnlyList<SceneImageRequest>>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("batch-prompts");
        claude.Setup(c => c.BatchEnded("batch-prompts", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        claude.Setup(c => c.SceneImagePromptResults("batch-prompts", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BatchItem<string>(2, "Prompt for scene two.", null), new BatchItem<string>(1, "Prompt for scene one.", null)]);

        SeedManuscript(TwoScenes);
        await DriveToCompletion(NewProcessor());

        claude.Verify(c => c.DescribeSceneImage(It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never());
        using var context = database.NewContext();
        var prompts = context.Scenes.OrderBy(scene => scene.Number).Select(scene => scene.ImagePrompt).ToList();
        Assert.Equal(["Prompt for scene one.", "Prompt for scene two."], prompts);
    }

    // The scene pictures on Gemini's batch tier (owner's request).

    private void PicturesBatchAs(string name, Func<IReadOnlyList<SceneImageJob>, GeminiBatchPoll> result, List<IReadOnlyList<SceneImageJob>> submitted)
    {
        IReadOnlyList<SceneImageJob> last = [];
        gemini.Setup(g => g.SubmitSceneImages(It.IsAny<IReadOnlyList<SceneImageJob>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<SceneImageJob> jobs, string _, CancellationToken _) =>
            {
                submitted.Add(jobs);
                last = jobs;
                return Task.FromResult(name);
            });
        gemini.Setup(g => g.PollSceneImages(name, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(result(last)));
    }

    /// <summary>A book's fresh scenes are painted in a batch, not one call each, and each picture lands on its own scene.</summary>
    [Fact]
    public async Task ScenePicturesArePaintedInABatch()
    {
        var submitted = new List<IReadOnlyList<SceneImageJob>>();
        PicturesBatchAs("batches/one", jobs => new GeminiBatchPoll(true,
            [.. jobs.Select(job => new BatchItem<byte[]>(job.SceneNumber, SampleImages.Png(), null))]), submitted);

        var id = SeedManuscript(TwoScenes);
        await DriveToCompletion(NewProcessor());

        Assert.Equal([1, 2], Assert.Single(submitted).Select(job => job.SceneNumber));
        gemini.Verify(g => g.GenerateSceneImage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
            It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Never());
        using var context = database.NewContext();
        Assert.All(context.Scenes.Where(scene => scene.NovelId == id).ToList(), scene =>
        {
            Assert.Equal(WorkStatus.Completed, scene.ImageStatus);
            Assert.True(File.Exists(scene.ImagePath));
        });
        var batch = Assert.Single(context.ModelBatches, row => row.Kind.StartsWith(BatchKinds.SceneImagesPrefix));
        Assert.Equal(BatchKinds.SceneImagesPrefix + "1", batch.Kind);
        Assert.NotNull(batch.CompletedAtUtc);
    }

    /// <summary>A scene the batch did not paint (refused or errored) goes one call at a time afterwards.</summary>
    [Fact]
    public async Task AScenePictureTheBatchDidNotPaintGoesOneAtATime()
    {
        var submitted = new List<IReadOnlyList<SceneImageJob>>();
        PicturesBatchAs("batches/one", jobs => new GeminiBatchPoll(true,
        [
            new BatchItem<byte[]>(1, SampleImages.Png(), null),
            new BatchItem<byte[]>(2, null, "Gemini batch item failed: internal"),
        ]), submitted);

        SeedManuscript(TwoScenes);
        await DriveToCompletion(NewProcessor());

        Assert.Single(submitted);
        gemini.Verify(g => g.GenerateSceneImage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
            It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    /// <summary>
    /// If Gemini refuses the batch, or a batch comes back with no pictures at all, the draft's pictures go one
    /// at a time from then on rather than batch after failing batch.
    /// </summary>
    [Theory]
    [InlineData("refused")]
    [InlineData("empty")]
    public async Task AFailingBatchTierTurnsBatchingOffForTheDraft(string how)
    {
        var submits = 0;
        gemini.Setup(g => g.SubmitSceneImages(It.IsAny<IReadOnlyList<SceneImageJob>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                submits++;
                return how == "refused"
                    ? Task.FromException<string>(new HttpRequestException("400 model not supported in batch", null, System.Net.HttpStatusCode.BadRequest))
                    : Task.FromResult("batches/empty");
            });
        gemini.Setup(g => g.PollSceneImages("batches/empty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiBatchPoll(true, []));

        var id = SeedManuscript(TwoScenes);
        await DriveToCompletion(NewProcessor());

        Assert.Equal(1, submits);
        gemini.Verify(g => g.GenerateSceneImage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
            It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        using var context = database.NewContext();
        Assert.Contains(context.ModelBatches, row => row.NovelId == id && row.Kind == BatchKinds.SceneImagesOff);
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == id).Stage);
    }

    [Fact]
    public async Task EveryPassRunsOnceAndTheSynthesisSeesThemAll()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        Assert.Equal(
            PersonaCatalogue.Sections.Select(persona => persona.Id).ToList(), sectionReads);
        Assert.Equal(
            PersonaCatalogue.Readers.Select(persona => persona.Id).ToList(), readerReads);
        // The verdict pass is gone: the round table replaces it (R32).
        Assert.Equal(["todo"], synthesisReads);

        Assert.Equal(8, sectionsSeenBySynthesis);
        Assert.Equal(7, readersSeenBySynthesis);

        // The plan is shown every finding written on the manuscript, keyed, so each item can name the
        // findings it draws on and link to them (R15).
        Assert.Contains(findingsSeenByPlan, entry => entry.Text.Contains("How did the gate open?", StringComparison.Ordinal));
        Assert.Contains(findingsSeenByPlan, entry => entry.Text.Contains("He felt the cold.", StringComparison.Ordinal));
        Assert.All(findingsSeenByPlan, entry => Assert.Matches("^f[0-9a-f]{8}$", entry.Key));

        // One row per pass, all settled, so a re-run bills nothing again.
        using var context = database.NewContext();
        var rows = context.PassReads.Where(pass => pass.NovelId == id).ToList();
        Assert.Equal(ReadingPlan.Passes.Count, rows.Count);
        Assert.All(rows, row => Assert.NotNull(row.Json));
    }

    /// <summary>
    /// The round table runs after the reads (R28), every reader on the panel, and is kept on the draft
    /// turn by turn: the host's welcome, the reader it handed to, the host's close and the summary.
    /// </summary>
    [Fact]
    public async Task TheRoundTableRunsAfterTheReadsAndIsKeptOnTheDraft()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        Assert.Equal(PersonaCatalogue.Readers.Select(persona => persona.Id), roundTablePanel.Select(review => review.PersonaId));
        using var context = database.NewContext();
        var table = RoundTableCodec.Read(context.BookAnalyses.Single(a => a.NovelId == id).RoundTableJson);
        Assert.True(table.Closed);

        // The panel agrees (every reader scored 7), so the show goes straight to the open discussion. The host
        // asks to close early on every move after the first; each early close raises the next agenda topic
        // instead, a reader answers it, and only once every topic has had its exchange does the show close.
        var agenda = table.Agenda.Select(item => item.Topic).ToList();
        Assert.Equal(["Structure", "Themes", "Characters"], agenda.Take(3));
        Assert.Contains("Pacing", agenda);
        Assert.Equal(agenda, table.Covered);
        Assert.Equal(1 + agenda.Count, speakCalls);
        Assert.Equal(agenda, table.Turns.Where(turn => turn.Speaker == RoundTablePlan.HostId && turn.Topic.Length > 0)
            .Select(turn => turn.Topic).Distinct());
        Assert.Equal("Goodnight, everyone.", table.Turns[^1].Words);
        Assert.Equal(["Whether it sells"], table.Summary!.StillDiffer);
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == id).Stage);

        // And the report prints it in the verdict's place.
        var report = ReportText(id);
        Assert.Contains("Hugo Hook says the gate scene sold them.", report, StringComparison.Ordinal);
        Assert.Contains("Where they still differ", report, StringComparison.Ordinal);
    }

    /// <summary>A turn that fails is tried again on its own: the show resumes, it does not start over.</summary>
    [Fact]
    public async Task AFailedTurnIsTriedAgainWithoutRestartingTheShow()
    {
        var failed = false;
        claude.Setup(c => c.RoundTableSpeak(
                It.IsAny<Persona>(), It.IsAny<IReadOnlyList<Review>>(), It.IsAny<RoundTable>(), It.IsAny<RoundTableCue>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                speakCalls++;
                if (!failed)
                {
                    failed = true;
                    return Task.FromException<string>(new HttpRequestException("connection dropped"));
                }
                return Task.FromResult("Second time lucky.");
            });

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        // The failed turn was tried again on its own: the host was not asked again for it, and the reader's
        // first line is the retried one.
        using var context = database.NewContext();
        var table = RoundTableCodec.Read(context.BookAnalyses.Single(a => a.NovelId == id).RoundTableJson);
        Assert.True(table.Closed);
        Assert.Equal(hostCalls, table.Turns.Count(turn => turn.Speaker == RoundTablePlan.HostId) - 1);
        Assert.Equal(speakCalls - 1, table.Turns.Count(turn => turn.Speaker != RoundTablePlan.HostId));
        Assert.Equal("Second time lucky.", table.Turns.First(turn => turn.Speaker != RoundTablePlan.HostId).Words);
    }

    // Release R: the books already reviewed are regenerated in place, at the owner's word.
    private ProjectService Projects() => new(database, new Mock<IGoogleDocsClient>().Object,
        new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
        clock, Options.Create(new RateLimitOptions()));

    /// <summary>
    /// A finished book's latest draft gets a fresh report in place: the same draft, read again, the round
    /// table run again, and its Drive copy replaced under the same name.
    /// </summary>
    [Fact]
    public async Task RegeneratingAFinishedBookRewritesItsReportInPlace()
    {
        var drive = new Mock<IPaperDriveArchive>();
        drive.Setup(d => d.FindParentFolder(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("folder-one");
        drive.Setup(d => d.UploadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file");
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { Enabled = true }, driveArchive: drive.Object));
        readerReads.Clear();
        hostCalls = 0;

        Assert.Equal(1, await Projects().StartRegenerationAsync(CancellationToken.None));
        using (var waiting = database.NewContext())
            Assert.True(waiting.Novels.Single(n => n.Id == id).Regenerating);
        await DriveToCompletion(NewProcessor(new ArchiveOptions { Enabled = true }, driveArchive: drive.Object));

        Assert.Equal(PersonaCatalogue.Readers.Select(persona => persona.Id), readerReads);
        Assert.True(hostCalls > 0);
        drive.Verify(d => d.UploadFile("folder-one", DeliveredDocument.FileName("Quest", 1), "application/pdf",
            It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        using var context = database.NewContext();
        var novel = Assert.Single(context.Novels);
        Assert.Equal(id, novel.Id);
        Assert.Equal(1, novel.DraftNumber);
        Assert.Equal(ProjectStage.Completed, novel.Stage);
        Assert.False(novel.Regenerating);
        Assert.NotNull(novel.RegeneratedAtUtc);
        Assert.True(RoundTableCodec.Read(context.BookAnalyses.Single(a => a.NovelId == id).RoundTableJson).Closed);
    }

    /// <summary>
    /// Regeneration sends nothing to the tablet and leaves its delivery mark as it was (owner's ruling), and
    /// never touches the voice profile: the manuscripts shaped it once already.
    /// </summary>
    [Fact]
    public async Task RegenerationSendsNothingToTheTabletAndLeavesTheVoiceProfileAlone()
    {
        var remarkable = new Mock<IRemarkableClient>();
        SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));
        remarkable.Invocations.Clear();
        bool delivered;
        using (var before = database.NewContext())
            delivered = before.BookAnalyses.Single().DeliveredToRemarkable;

        var voice = new Mock<Storyworthtelling.Voice.IVoiceClient>();
        var profiles = new Mock<Storyworthtelling.Voice.IVoiceProfileStore>();
        await Projects().StartRegenerationAsync(CancellationToken.None);
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object,
            voiceClient: voice.Object, voiceStore: profiles.Object));

        Assert.Empty(remarkable.Invocations);
        Assert.Empty(voice.Invocations);
        Assert.Empty(profiles.Invocations);
        using var context = database.NewContext();
        Assert.Equal(delivered, context.BookAnalyses.Single().DeliveredToRemarkable);
        Assert.Equal(ProjectStage.Completed, context.Novels.Single().Stage);
    }

    /// <summary>A scene whose text is unchanged keeps its picture: regeneration does not pay for it twice.</summary>
    [Fact]
    public async Task RegenerationKeepsThePictureOfEverySceneWhoseTextIsUnchanged()
    {
        SeedManuscript("The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.");
        await DriveToCompletion(NewProcessor());
        gemini.Invocations.Clear();
        judgedScenes.Clear();

        await Projects().StartRegenerationAsync(CancellationToken.None);
        await DriveToCompletion(NewProcessor());

        gemini.Verify(g => g.GenerateSceneImage(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()),
            Times.Never());
        Assert.Equal([1, 2], judgedScenes.Select(scene => scene.Number).Order());
        using var context = database.NewContext();
        Assert.All(context.Scenes.ToList(), scene =>
        {
            Assert.Equal(WorkStatus.Completed, scene.ImageStatus);
            Assert.True(File.Exists(scene.ImagePath));
        });
    }

    /// <summary>A series is regenerated in story order, so each book's bible is rebuilt before the next is read.</summary>
    [Fact]
    public async Task ASeriesIsRegeneratedInStoryOrder()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        var second = SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        using (var context = database.NewContext())
        {
            var sequel = context.Novels.Single(n => n.Id == second);
            // As the app's own "next book" does, book one joins the series.
            context.Novels.Single(n => n.Id == first).SeriesId = sequel.SeriesId;
            sequel.Title = "Quest Before";
            // Book two is a prequel: it takes place before book one, so it goes first.
            sequel.PlacementJson = StoryPlacementCodec.Write(StoryPlacement.Before("Quest").Confirm());
            context.SaveChanges();
        }
        await DriveToCompletion(NewProcessor());
        var order = new List<string>();
        claude.Setup(c => c.ReadSeriesBible(
                It.IsAny<string>(), It.IsAny<ReadingFrame>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, ReadingFrame _, string title, string _, CancellationToken _) =>
            {
                order.Add(title);
                return Task.FromResult<IReadOnlyList<BibleFact>>([]);
            });

        Assert.Equal(2, await Projects().StartRegenerationAsync(CancellationToken.None));
        await DriveToCompletion(NewProcessor());

        Assert.Equal(["Quest Before", "Quest"], order);
    }

    [Fact]
    public async Task OnlyEachBooksLatestFinishedDraftIsOffered()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        using (var context = database.NewContext())
        {
            var draftTwo = new Novel
            {
                Title = "Quest", GoogleDocId = "doc", ProjectId = first, DraftNumber = 2, PreviousDraftId = first,
                Stage = ProjectStage.Ingesting, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
            };
            context.Novels.Add(draftTwo);
            context.SaveChanges();
        }

        // Draft two is still being read: the book has nothing to regenerate until it finishes.
        Assert.Empty(await Projects().RegenerationCandidatesAsync(CancellationToken.None));
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
    // The fourteen whole-book passes and the reduce calls each send the whole book to Claude. A failure used to be
    // recorded nowhere but the manuscript's one-line error, was retried four seconds later with no limit
    // (StageAttempts documented a cap that nothing applied), and the next worker cycle started again
    // immediately — so a call that kept dying mid-stream was re-billed all day with nothing in the log.

    private static ReadingPass FirstPass => ReadingPlan.Passes[0];

    private void FailPass(ReadingPass pass, Func<int, Exception?> failure, List<int>? calls = null)
    {
        var count = 0;
        claude.Setup(c => c.ReadSection(
                It.Is<Persona>(persona => persona.Id == pass.Id), It.IsAny<string>(), It.IsAny<ReadingFrame>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, string _, ReadingFrame _, CancellationToken _) =>
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

    // The archive to Drive and the delivery to the tablet are the last steps, and the phone is as likely
    // to be offline then as at any other. Counting each offline failure as a try spent the whole budget
    // in seconds and settled the book as "not archived" / "did not reach the tablet".

    private static Mock<IPaperDriveArchive> DriveFailingOffline(int offlineTimes, List<int> calls)
    {
        var drive = new Mock<IPaperDriveArchive>();
        drive.Setup(d => d.FindParentFolder(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("folder-one");
        drive.Setup(d => d.UploadFile(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add(calls.Count + 1);
                return calls.Count <= offlineTimes
                    ? Task.FromException<string>(Offline())
                    : Task.FromResult("archived-file-one");
            });
        return drive;
    }

    [Fact]
    public async Task AnOfflinePhoneNeverSpendsTheArchivesAttempts()
    {
        var id = SeedManuscript();
        var calls = new List<int>();
        var drive = DriveFailingOffline(10, calls);

        await DriveToCompletion(NewProcessor(driveArchive: drive.Object));

        Assert.Equal(11, calls.Count);
        Assert.Equal(ProjectStage.Completed, LoadNovel(id).Stage);
        Assert.Equal("archived-file-one", AnalysisOf(id).ArchivedDriveFileId);
    }

    [Fact]
    public async Task AnOfflineArchiveWaitsBeforeAskingAgain()
    {
        SeedManuscript();
        var calls = new List<int>();
        var drive = DriveFailingOffline(int.MaxValue, calls);

        await DriveToFirstCall(NewProcessor(driveArchive: drive.Object), calls);
        var result = ProcessResult.Idle;
        for (var cycle = 0; cycle < 5; cycle++)
            result = await NewProcessor(driveArchive: drive.Object).ProcessNextAsync(CancellationToken.None);

        Assert.Single(calls);
        Assert.Equal(ProcessOutcome.Waiting, result.Outcome);
        Assert.True(result.Wait > TimeSpan.FromSeconds(30), $"Waited only {result.Wait}");
    }

    [Fact]
    public async Task AnOfflinePhoneNeverSpendsTheTabletsAttempts()
    {
        var id = SeedManuscript();
        var calls = 0;
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls <= 10 ? Task.FromException(Offline()) : Task.CompletedTask);

        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));

        Assert.Equal(11, calls);
        var analysis = AnalysisOf(id);
        Assert.True(analysis.DeliveredToRemarkable);
        Assert.Null(analysis.RemarkableNote);
        Assert.Equal(ProjectStage.Completed, LoadNovel(id).Stage);
    }

    [Fact]
    public async Task ATabletThatTurnsTheUploadAwayIsNamedNotClaude()
    {
        var id = SeedManuscript();
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized)."));

        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));

        var note = AnalysisOf(id).RemarkableNote;
        Assert.DoesNotContain("Claude", note);
        Assert.Contains("reMarkable refused the request", note);
    }


    // A reply cut off at the output limit comes back cut off again: it is the same request with the same
    // limit. Five repeats of a whole-book read bought nothing five times, so the book is parked after two.
    [Fact]
    public async Task ATruncatedReadIsNotRepeatedMoreThanTwice()
    {
        var id = SeedManuscript();
        var calls = new List<int>();
        FailPass(FirstPass, _ => new ResponseTruncatedException(
            "Claude response was truncated at the 8000-token output limit"), calls);
        var logger = new CapturingLogger<ReviewProcessor>();

        await Cycle(NewProcessor(logger: logger), 200);

        Assert.Equal(2, calls.Count);
        var novel = LoadNovel(id);
        Assert.Equal(ProjectStage.ComposingAnalysis, novel.Stage);
        Assert.Contains(FirstPass.Id, novel.LastError);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("giving up") &&
                                             e.Message.Contains("truncat"));
    }

    [Fact]
    public async Task ATruncationThenASuccessStillCompletes()
    {
        var id = SeedManuscript();
        FailPass(FirstPass, attempt => attempt == 1
            ? new ResponseTruncatedException("Claude response was truncated at the 8000-token output limit")
            : null);

        await DriveToCompletion(NewProcessor());

        Assert.Equal(ProjectStage.Completed, LoadNovel(id).Stage);
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

    /// <summary>
    /// A second draft's report says how each reader's score moved, against that reader's score on the
    /// draft it was made from — read back from the first draft's own stored reads.
    /// </summary>
    [Fact]
    public async Task SecondDraft_ReportSaysHowEachReadersScoreMoved()
    {
        const string text = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        var firstId = SeedManuscript(text);
        await DriveToCompletion(NewProcessor());
        Assert.DoesNotContain("last draft", ReportText(firstId), StringComparison.Ordinal);

        var secondId = SeedNextDraft(firstId, text);
        await DriveToCompletion(NewProcessor());

        Assert.Contains("unchanged from last draft", ReportText(secondId), StringComparison.Ordinal);
    }

    private string ReportText(Guid draftId)
    {
        using var context = database.NewContext();
        var path = context.BookAnalyses.Single(a => a.NovelId == draftId).PdfPath;
        Assert.NotNull(path);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
        return string.Join("\n", pdf.GetPages().Select(page => page.Text));
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

    /// <summary>The second draft's plan pass is shown the first draft's plan to compare against (R19).</summary>
    [Fact]
    public async Task SecondDraft_ThePlanPassSeesTheLastPlan()
    {
        const string text = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        var firstId = SeedManuscript(text);
        await DriveToCompletion(NewProcessor());
        Assert.Empty(previousPlanSeen);

        SeedNextDraft(firstId, text);
        await DriveToCompletion(NewProcessor());

        Assert.Equal("The middle", Assert.Single(previousPlanSeen).Issue);
    }

    /// <summary>
    /// A changed scene's block prints the last draft's verdict beside this one's (R19), read off the scene it
    /// was matched to; an unchanged scene's block does not, since nothing about it moved.
    /// </summary>
    [Fact]
    public async Task SecondDraft_AChangedScenesBlockCarriesItsLastVerdict()
    {
        const string first = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        const string second = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked loudly and waited in the cold.";
        var firstId = SeedManuscript(first);
        await DriveToCompletion(NewProcessor());

        var secondId = SeedNextDraft(firstId, second);
        await DriveToCompletion(NewProcessor());

        var text = ReportText(secondId);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "Last draft"));
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
        claude.Setup(c => c.ReadSection(It.IsAny<Persona>(), It.IsAny<string>(), It.IsAny<ReadingFrame>(), It.IsAny<CancellationToken>()))
            .Returns((Persona persona, string _, ReadingFrame _, CancellationToken _) => Task.FromResult(SectionRead.Silent(persona.Id)));

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

        // Streamed from the file (efficiency audit §18): a long report was held whole in memory, twice over.
        // Delivery rides the proven root path Foreword uses (the "Canonizer" folder path relied on a
        // reMarkable wire format never verified against the live cloud, and silently delivered nothing).
        // Named through the same function the Drive upload uses, so the two targets cannot come to
        // different names for one document — which is what a hand-written "Analysis" here let happen.
        remarkable.Verify(
            r => r.Upload(DeliveredDocument.Name("Quest", 1), It.IsAny<Stream>(), It.IsAny<long>(), null, It.IsAny<CancellationToken>()), Times.Once);
        remarkable.Verify(r => r.EnsureFolder(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

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

        remarkable.Verify(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Review_StillCompletes_WhenRemarkableDeliveryKeepsFailing()
    {
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("reMarkable is down"));

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));

        using var context = database.NewContext();
        // Delivery gave up after its budget, but the review still reached Completed (the report is on disk).
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == id).Stage);
        Assert.True(context.BookAnalyses.Single(a => a.NovelId == id).DeliveredToRemarkable);   // settled
    }


    // ---- Delivery to the tablet ------------------------------------------------------------------------
    //
    // The first full book rendered a 173 MB report. The reMarkable upload buffers the whole file in
    // memory, ran the phone out of it, and was then tried five more times with the same file. The
    // outcome was a line in a log: the Drive upload that followed cleared the recorded error, so
    // nothing on the desk said the tablet had not got it.

    private BookAnalysis AnalysisOf(Guid id)
    {
        using var context = database.NewContext();
        return context.BookAnalyses.Single(a => a.NovelId == id);
    }

    [Fact]
    public async Task AnUploadThatRunsOutOfMemoryIsNotRepeated()
    {
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RemarkableRefusedException(RemarkableRefusal.OutOfMemory, 5_000_000, new Exception(
                "Failed to allocate a 8208 byte allocation with 203360 free bytes and 198KB until OOM, " +
                "target footprint 268435456, growth limit 268435456")));
        var logger = new CapturingLogger<ReviewProcessor>();

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object, logger: logger));

        remarkable.Verify(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once());
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == id).Stage);
        var analysis = AnalysisOf(id);
        Assert.True(analysis.DeliveredToRemarkable);   // settled, not retried
        Assert.Contains("memory", analysis.RemarkableNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("not repeating"));
    }

    [Fact]
    public async Task AReportOverTheTabletsLimitIsNotEvenAttempted()
    {
        var remarkable = new Mock<IRemarkableClient>();

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor(
            new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object,
            store: new OversizedReports(new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })))));

        remarkable.Verify(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never());
        var analysis = AnalysisOf(id);
        Assert.True(analysis.DeliveredToRemarkable);
        Assert.Contains("MB", analysis.RemarkableNote);
        Assert.Contains("limit", analysis.RemarkableNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADeliveryThatGaveUpLeavesANoteEvenAfterDriveSucceeds()
    {
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("reMarkable is down"));

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));

        Assert.False(string.IsNullOrWhiteSpace(AnalysisOf(id).RemarkableNote));
    }

    [Fact]
    public async Task ASuccessfulDeliveryLeavesNoNote()
    {
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));

        Assert.Null(AnalysisOf(id).RemarkableNote);
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

    /// <summary>A sequel's readers are given the earlier book's canon (R27); a first book's are given none.</summary>
    [Fact]
    public async Task ASequelsReadersGetTheEarlierBooksCanon()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        Assert.All(readerFrames, frame => Assert.Equal(string.Empty, frame.SeriesCanon));

        readerFrames.Clear();
        SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        await DriveToCompletion(NewProcessor());

        Assert.Equal(PersonaCatalogue.Readers.Count, readerFrames.Count);
        Assert.All(readerFrames, frame => Assert.Contains("Quest", frame.SeriesCanon, StringComparison.Ordinal));
    }

    /// <summary>
    /// The series bible (release G): book two's read is merged into its series' bible, which is seeded
    /// first from book one's stored canon rather than a second read of book one.
    /// </summary>
    [Fact]
    public async Task ASequelIsReadIntoItsSeriesBibleSeededFromTheEarlierBooksCanon()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        Assert.Empty(biblesSeen);

        var second = SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        await DriveToCompletion(NewProcessor());

        // Once, shown what book one established.
        var seen = Assert.Single(biblesSeen);
        Assert.Contains("\"Quest\"", seen, StringComparison.Ordinal);

        using var context = database.NewContext();
        Assert.True(context.BookAnalyses.Single(a => a.NovelId == second).SeriesBibleUpdated);
        var record = Assert.Single(context.SeriesBibles);
        var bible = SeriesBibleCodec.Read(record.Json);
        Assert.Equal(2, bible.Books.Count);
        var hero = Assert.Single(bible.Entries, entry => entry.Kind == BibleKind.Character && entry.Name == "Hero");
        Assert.Equal(2, hero.States.Count);
        Assert.Equal("Now a knight errant.", hero.Current);
        Assert.Equal("The knight's son rode out", hero.States[1].Quote);
    }

    /// <summary>A sequel's place in the series is guessed once, from the other books, and kept as a guess (R21).</summary>
    [Fact]
    public async Task ASequelsPlaceInTheSeriesIsGuessedAndKeptUnconfirmed()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        Assert.Empty(placementBooks);

        var second = SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        await DriveToCompletion(NewProcessor());

        Assert.Equal(["Quest"], Assert.Single(placementBooks));
        using var context = database.NewContext();
        var placement = StoryPlacementCodec.Read(context.Novels.Single(n => n.Id == second).PlacementJson);
        Assert.NotNull(placement);
        Assert.Equal("after \u201CQuest\u201D", placement!.Where);
        Assert.False(placement.Confirmed);
    }

    /// <summary>A placement the owner confirmed, carried onto a later draft, is not guessed again.</summary>
    [Fact]
    public async Task AConfirmedPlacementIsNotGuessedAgain()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        var second = SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        using (var context = database.NewContext())
        {
            context.Novels.Single(n => n.Id == second).PlacementJson =
                StoryPlacementCodec.Write(StoryPlacement.Before("Quest").Confirm());
            context.SaveChanges();
        }

        await DriveToCompletion(NewProcessor());

        Assert.Empty(placementBooks);
        using var check = database.NewContext();
        Assert.True(StoryPlacementCodec.Read(check.Novels.Single(n => n.Id == second).PlacementJson)!.Confirmed);
    }

    /// <summary>
    /// A sequel is checked against the series bible (R24), seeded from the earlier book, with its guessed
    /// place in the series (R21); its readers are given the bible too (R27).
    /// </summary>
    [Fact]
    public async Task ASequelsContinuityReadAndReadersAreGivenTheSeriesBibleAndItsPlacement()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        string? bibleSent = null;
        StoryPlacement? placementSent = null;
        claude.Setup(c => c.AssessSeriesContinuity(
                It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<string>(), It.IsAny<StoryPlacement?>(),
                It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, SeriesCanon canon, string bible, StoryPlacement? placement, IReadOnlyList<ChapterNotes> _, string _, CancellationToken _) =>
            {
                bibleSent = bible;
                placementSent = placement;
                return Task.FromResult(SeriesContinuity.Empty with { PriorBookTitle = canon.PriorBookTitle });
            });

        readerFrames.Clear();
        SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        await DriveToCompletion(NewProcessor());

        Assert.StartsWith("SERIES BIBLE", bibleSent, StringComparison.Ordinal);
        Assert.Contains("\"Quest\"", bibleSent, StringComparison.Ordinal);
        Assert.Equal("after \u201CQuest\u201D", placementSent!.Where);
        Assert.All(readerFrames, frame => Assert.StartsWith("SERIES BIBLE", frame.SeriesCanon, StringComparison.Ordinal));
    }

    /// <summary>
    /// The series bible is printed and sent beside the report (R26): to the tablet's root and to the
    /// Drive folder the report went to, once per draft.
    /// </summary>
    [Fact]
    public async Task ASequelsSeriesBibleIsDeliveredToTheTabletAndDrive()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        var second = SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        using (var context = database.NewContext())
        {
            foreach (var novel in context.Novels)
                novel.SeriesName = "The Gate";
            context.SaveChanges();
        }
        var remarkable = new Mock<IRemarkableClient>();
        var drive = new Mock<IPaperDriveArchive>();
        drive.Setup(d => d.FindParentFolder(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("folder-one");
        drive.Setup(d => d.UploadFile(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file");
        string? docText = null;
        drive.Setup(d => d.WriteGoogleDoc(null, "folder-one", "The Gate — Series bible", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string? _, string _, string _, string text, CancellationToken _) => docText = text)
            .ReturnsAsync("bible-doc");

        await DriveToCompletion(NewProcessor(
            new ArchiveOptions { DeliverToRemarkable = true, Enabled = true }, remarkable.Object, drive.Object));

        var name = DeliveredDocument.BibleName("The Gate", "Quest", 1);
        remarkable.Verify(r => r.Upload(name, It.IsAny<Stream>(), It.IsAny<long>(), null, It.IsAny<CancellationToken>()), Times.Once);
        // Drive keeps one versioned Google Doc for the series rather than a PDF per draft (owner's request).
        drive.Verify(d => d.UploadFile(It.IsAny<string>(), name + ".pdf", It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("This version: after Quest, draft 1", docText);
        using var check = database.NewContext();
        Assert.Equal("bible-doc", check.SeriesBibles.Single().DriveDocId);
        Assert.True(check.BookAnalyses.Single(a => a.NovelId == second).SeriesBibleDelivered);
        Assert.Equal(ProjectStage.Completed, check.Novels.Single(n => n.Id == second).Stage);
    }

    /// <summary>A bible that fails to send is given up on, and the review still finishes.</summary>
    [Fact]
    public async Task ABibleThatWillNotSendIsGivenUpOnAndTheReviewFinishes()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        var second = SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.Is<string>(title => title.Contains("Series bible")), It.IsAny<Stream>(),
                It.IsAny<long>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("refused"));

        await DriveToCompletion(NewProcessor(new ArchiveOptions { DeliverToRemarkable = true }, remarkable.Object));

        using var check = database.NewContext();
        Assert.True(check.BookAnalyses.Single(a => a.NovelId == second).SeriesBibleDelivered);
        Assert.Equal(ProjectStage.Completed, check.Novels.Single(n => n.Id == second).Stage);
    }

    /// <summary>A correction the owner makes while the bible read is out is kept, not written over (R26).</summary>
    [Fact]
    public async Task ACorrectionMadeDuringTheBibleReadIsKept()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        var second = SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        Guid series;
        using (var context = database.NewContext())
            series = context.Novels.Single(n => n.Id == second).SeriesId!.Value;

        claude.Setup(c => c.ReadSeriesBible(
                It.IsAny<string>(), It.IsAny<ReadingFrame>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                // The owner corrects the castle while the read is out.
                using var context = database.NewContext();
                var bible = SeriesBible.Empty
                    .WithBook(new SeriesBook(first, "Quest", 1), [new BibleFact(BibleKind.Setting, "Castle", [], "", "Grey stone.", "")])
                    .Correct(BibleKind.Setting, "Castle", "Black basalt.");
                context.SeriesBibles.Add(new SeriesBibleRecord { SeriesId = series, Json = SeriesBibleCodec.Write(bible), UpdatedAtUtc = clock.UtcNow });
                context.SaveChanges();
                return Task.FromResult<IReadOnlyList<BibleFact>>(
                    [new BibleFact(BibleKind.Setting, "Castle", [], "", "Ruined.", "")]);
            });

        await DriveToCompletion(NewProcessor());

        using var check = database.NewContext();
        var castle = Assert.Single(SeriesBibleCodec.Read(check.SeriesBibles.Single().Json).Entries,
            entry => entry.Kind == BibleKind.Setting && entry.Name == "Castle");
        Assert.Equal("Black basalt.", castle.Correction);
        Assert.Contains(castle.States, state => state.Text == "Ruined.");
    }

    /// <summary>
    /// After a reset the phone has the books but no bible: the sequel's review restores it from Drive before
    /// the continuity read, so the owner's corrections are what the book is checked against.
    /// </summary>
    [Fact]
    public async Task ASequelOnAResetPhoneRestoresTheBibleFromDriveBeforeTheContinuityRead()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        var oldPhone = SeriesBible.Empty
            .WithBook(new SeriesBook(Guid.NewGuid(), "Quest", 1), [new BibleFact(BibleKind.Setting, "Castle", [], "", "Grey stone.", "")])
            .Correct(BibleKind.Setting, "Castle", "Black basalt, as the author corrected it.");
        var drive = new Mock<IPaperDriveArchive>();
        drive.Setup(d => d.FindParentFolder(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("folder-one");
        drive.Setup(d => d.UploadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file");
        drive.Setup(d => d.FindFileByName("Quest — Series bible (data).json", It.IsAny<CancellationToken>())).ReturnsAsync("data-9");
        drive.Setup(d => d.ReadTextFile("data-9", It.IsAny<CancellationToken>())).ReturnsAsync(SeriesBibleCodec.Write(oldPhone));
        drive.Setup(d => d.WriteTextFile(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string? id, string _, string _, string _, string _, CancellationToken _) => Task.FromResult(id ?? "data-new"));
        string? bibleSent = null;
        claude.Setup(c => c.AssessSeriesContinuity(
                It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<string>(), It.IsAny<StoryPlacement?>(),
                It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, SeriesCanon canon, string bible, StoryPlacement? _, IReadOnlyList<ChapterNotes> _, string _, CancellationToken _) =>
            {
                bibleSent = bible;
                return Task.FromResult(SeriesContinuity.Empty with { PriorBookTitle = canon.PriorBookTitle });
            });

        SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        await DriveToCompletion(NewProcessor(new ArchiveOptions { Enabled = true }, driveArchive: drive.Object));

        Assert.Contains("Black basalt, as the author corrected it.", bibleSent, StringComparison.Ordinal);
        // The backup after the review rewrites the restored data file, not a new one.
        drive.Verify(d => d.WriteTextFile("data-9", It.IsAny<string>(), "Quest — Series bible (data).json", "application/json",
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        using var context = database.NewContext();
        Assert.Equal("data-9", context.SeriesBibles.Single().DriveDataId);
    }

    [Fact]
    public async Task AStandaloneBookHasNoSeriesBible()
    {
        SeedManuscript();
        await DriveToCompletion(NewProcessor());

        Assert.Empty(biblesSeen);
        using var context = database.NewContext();
        Assert.Empty(context.SeriesBibles);
    }

    /// <summary>A stored bible that will not read is never written over: the review finishes without it.</summary>
    [Fact]
    public async Task AnUnreadableBibleIsLeftAloneAndTheReviewStillFinishes()
    {
        var first = SeedManuscript();
        await DriveToCompletion(NewProcessor());
        var second = SeedNextBook(first, "The knight's son rode out. At the gate, he knocked.");
        Guid series;
        using (var context = database.NewContext())
        {
            series = context.Novels.Single(n => n.Id == second).SeriesId!.Value;
            context.SeriesBibles.Add(new SeriesBibleRecord { SeriesId = series, Json = "damaged", UpdatedAtUtc = clock.UtcNow });
            context.SaveChanges();
        }

        await DriveToCompletion(NewProcessor());

        Assert.Empty(biblesSeen);
        using var check = database.NewContext();
        Assert.Equal("damaged", check.SeriesBibles.Single().Json);
        Assert.Equal(ProjectStage.Completed, check.Novels.Single(n => n.Id == second).Stage);
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
            It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<string>(), It.IsAny<StoryPlacement?>(), It.IsAny<IReadOnlyList<ChapterNotes>>(),
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
                It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<string>(), It.IsAny<StoryPlacement?>(), It.IsAny<IReadOnlyList<ChapterNotes>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, SeriesCanon canon, string _, StoryPlacement? _, IReadOnlyList<ChapterNotes> _, string _, CancellationToken _) =>
                Task.FromResult(new SeriesContinuity(
                    canon.PriorBookTitle,
                    [new SeriesBreak("Chapter 1", "The hero's eyes were grey", "Now they are green")],
                    [new CarriedThread("The gate's mechanism was never explained", "Ignored")])));

        var secondId = SeedNextBook(firstId, "A short tale. The hero walked to the castle and knocked on the gate.");
        await DriveToCompletion(NewProcessor());

        claude.Verify(c => c.AssessSeriesContinuity(
            It.IsAny<string>(), It.Is<SeriesCanon>(canon => canon.OpenThreads.Count > 0),
            It.IsAny<string>(), It.IsAny<StoryPlacement?>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

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
                It.IsAny<string>(), It.IsAny<SeriesCanon>(), It.IsAny<string>(), It.IsAny<StoryPlacement?>(), It.IsAny<IReadOnlyList<ChapterNotes>>(),
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

    // Reports every stored file as one byte over the tablet's limit, so the size refusal is reached without writing 100 MB.
    private sealed class OversizedReports(IImageStore inner) : IImageStore
    {
        public string Save(string relativePath, byte[] content) => inner.Save(relativePath, content);
        public byte[] Read(string absolutePath) => inner.Read(absolutePath);
        public bool Exists(string absolutePath) => inner.Exists(absolutePath);
        public long Size(string absolutePath) => RemarkableLimits.MaxBytes + 1;
        public void Delete(string relativePath) => inner.Delete(relativePath);
    }
}
