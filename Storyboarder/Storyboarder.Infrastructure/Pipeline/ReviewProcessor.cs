using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Domain.Text;
using Storyboarder.Domain.Time;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.External;
using Storyboarder.Infrastructure.Persistence;
using Storyworthtelling.Voice;

namespace Storyboarder.Infrastructure.Pipeline;

// The heart of the app: advances one manuscript by one unit of work per call, idempotently and
// resumably. State lives entirely in the database, so the worker resumes after pauses, rate limits, or
// restarts simply by querying the next due unit. A single worker calls ProcessNextAsync in a loop. The
// pipeline maps → merges the design bible, renders one wordless evidence image per scene, map/reduces the
// revision report, composes the report PDF, then archives it beside the source manuscript.
public sealed class ReviewProcessor : IPipelineStep
{
    private readonly IDbContextFactory<StoryboarderDbContext> dbContextFactory;
    private readonly IClaudeAnalysisClient claude;
    private readonly IGeminiImageClient gemini;
    private readonly IImageStore imageStore;
    private readonly IAnalysisPdfComposer analysisPdfComposer;
    private readonly IClock clock;
    private readonly ILogger<ReviewProcessor> logger;

    private readonly RequestRateLimiter claudeLimiter;
    private readonly RequestRateLimiter geminiLimiter;
    private readonly BackoffScheduler backoff;
    private readonly RateLimitOptions rateLimits;
    private readonly ArchiveOptions archive;

    // Null when the platform head supplies no Drive archive (and on any non-Android host). The archive
    // stage treats that exactly like archiving being switched off.
    private readonly IDriveArchive? driveArchive;

    // The owner's-voice learner + shared profile store. Both null on a head that wires no voice feature
    // (or on any non-Android host without a Claude key for it); voice refinement then simply doesn't run.
    private readonly IVoiceClient? voiceClient;
    private readonly IVoiceProfileStore? voiceStore;

    private readonly TextChunker chunker = new();
    private readonly ChapterDetector chapterDetector = new();

    // How much opening prose the art-style derivation sees — enough to read the genre, voice and setting
    // without paying for the whole book on a one-off, best-effort call.
    private const int ArtStyleSampleChars = 6000;

    // How much manuscript the voice refinement reads. Voice is about HOW the owner writes — cadence,
    // register, attitude — which a generous opening sample captures without paying to send a whole novel
    // to Claude just to distil a short profile.
    private const int VoiceSampleChars = 12000;

    // How much of each chapter the voice-consistency read samples. One representative opening passage per
    // chapter is enough to characterise that chapter's register and cadence and to catch a chapter that
    // drifts from the book's voice, while keeping the whole book's samples inside a single Claude call.
    private const int VoiceConsistencySampleChars = 800;
    private readonly DesignBibleMerger merger = new();

    public ReviewProcessor(
        IDbContextFactory<StoryboarderDbContext> dbContextFactory,
        IClaudeAnalysisClient claude,
        IGeminiImageClient gemini,
        IImageStore imageStore,
        IAnalysisPdfComposer analysisPdfComposer,
        IClock clock,
        IOptions<RateLimitOptions> rateLimitOptions,
        IOptions<ArchiveOptions> archiveOptions,
        ILogger<ReviewProcessor> logger,
        IDriveArchive? driveArchive = null,
        IVoiceClient? voiceClient = null,
        IVoiceProfileStore? voiceStore = null)
    {
        this.dbContextFactory = dbContextFactory;
        this.claude = claude;
        this.gemini = gemini;
        this.imageStore = imageStore;
        this.analysisPdfComposer = analysisPdfComposer;
        this.clock = clock;
        this.logger = logger;
        this.voiceClient = voiceClient;
        this.voiceStore = voiceStore;

        rateLimits = rateLimitOptions.Value;
        archive = archiveOptions.Value;
        this.driveArchive = driveArchive;
        claudeLimiter = new RequestRateLimiter(rateLimits.ClaudeRequestsPerMinute, rateLimits.ClaudeRequestsPerDay, clock);
        geminiLimiter = new RequestRateLimiter(rateLimits.GeminiRequestsPerMinute, rateLimits.GeminiRequestsPerDay, clock);
        backoff = new BackoffScheduler(
            TimeSpan.FromSeconds(rateLimits.BaseDelaySeconds), TimeSpan.FromSeconds(rateLimits.MaxDelaySeconds));
    }

    public async Task<ProcessResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Advance the oldest active manuscript that actually has runnable work. A manuscript that can't
        // make progress right now must not hog the worker (and stall every one behind it): both an Idle
        // (blocked on a failed unit) and a Waiting (parked on a rate-limit or retry window) are stepped
        // past so a manuscript with real work gets its turn. Only if none can do work do we return: the
        // soonest Waiting if any is merely deferred, else Idle.
        var novels = await db.Novels
            .Where(n => n.Stage != ProjectStage.Completed && n.Stage != ProjectStage.Failed &&
                        n.Stage != ProjectStage.Cancelled)
            .OrderBy(n => n.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        ProcessResult? soonestWait = null;
        foreach (var novel in novels)
        {
            var result = await Dispatch(db, novel, cancellationToken);
            if (result.Outcome == ProcessOutcome.DidWork)
                return result;
            if (result.Outcome == ProcessOutcome.Waiting &&
                (soonestWait is null || result.Wait < soonestWait.Value.Wait))
                soonestWait = result;
        }

        return soonestWait ?? ProcessResult.Idle;
    }

    private Task<ProcessResult> Dispatch(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken) =>
        novel.Stage switch
        {
            ProjectStage.Created => Advance(db, novel, cancellationToken),
            ProjectStage.Ingesting => Ingest(db, novel, cancellationToken),
            ProjectStage.BuildingDesignBible => BuildDesignBible(db, novel, cancellationToken),
            ProjectStage.RenderingSceneImages => RenderSceneImages(db, novel, cancellationToken),
            ProjectStage.ComposingAnalysis => ComposeAnalysis(db, novel, cancellationToken),
            ProjectStage.Archiving => ArchiveOutputs(db, novel, cancellationToken),
            _ => Task.FromResult(ProcessResult.Idle)
        };

    // Created -> Ingesting (the only transition with no work of its own).
    private async Task<ProcessResult> Advance(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        SetStage(novel, ProjectStage.Ingesting);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task<ProcessResult> Ingest(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var source = await db.SourceTexts.FirstOrDefaultAsync(s => s.NovelId == novel.Id, cancellationToken);
        if (source is null)
            return Fail(db, novel, "No source text was stored for this manuscript.", cancellationToken);

        if (!await db.Chunks.AnyAsync(c => c.NovelId == novel.Id, cancellationToken))
        {
            foreach (var span in chunker.Chunk(source.FullText))
                db.Chunks.Add(new Chunk
                {
                    NovelId = novel.Id,
                    Ordinal = span.Ordinal,
                    StartOffset = span.StartOffset,
                    EndOffset = span.EndOffset,
                    Text = span.Text
                });

            // Persist the chunks before labelling them: the labeller reads the stored rows (which is
            // what also lets it heal a conversion chunked before chapter detection existed), and an EF
            // query doesn't see inserts still sitting in the change tracker.
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsureChaptersLabelled(db, novel, source.FullText, cancellationToken);

        // Learn the owner's voice from this manuscript into the shared profile every com.storyworthtelling
        // app reads. Best-effort and guarded by VoiceAnalyzed, so it runs once per draft and never fails or
        // stalls the review. Persisted with the stage change below.
        await RefineOwnerVoice(novel, source.FullText, cancellationToken);

        SetStage(novel, ProjectStage.BuildingDesignBible);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // Folds this draft's manuscript into the owner's shared voice profile. No-op when no voice client/store
    // is wired or the draft has already been analyzed. A failure is swallowed (logged) and leaves
    // VoiceAnalyzed false so a later ingest can retry — voiced messaging is a nicety, never a gate on a review.
    private async Task RefineOwnerVoice(Novel novel, string manuscript, CancellationToken cancellationToken)
    {
        if (voiceClient is null || voiceStore is null || novel.VoiceAnalyzed)
            return;

        try
        {
            var sample = manuscript.Length > VoiceSampleChars ? manuscript[..VoiceSampleChars] : manuscript;
            var existing = await voiceStore.LoadAsync(cancellationToken);
            var refined = await voiceClient.RefineAsync(existing, sample, cancellationToken);
            await voiceStore.SaveAsync(refined, cancellationToken);
            novel.VoiceAnalyzed = true;
            logger.LogInformation(
                "Refined owner voice from manuscript {NovelId} ({Documents} document(s) now analyzed)",
                novel.Id, refined.DocumentsAnalyzed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Owner voice refinement failed for manuscript {NovelId}; continuing without it", novel.Id);
        }
    }

    private async Task<ProcessResult> BuildDesignBible(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // 1) Extract one pending/due chunk.
        var chunk = await db.Chunks
            .Where(c => c.NovelId == novel.Id &&
                        (c.AnalysisStatus == WorkStatus.Pending ||
                         (c.AnalysisStatus == WorkStatus.NeedsRetry && c.NextAttemptAtUtc <= now)))
            .OrderBy(c => c.Ordinal)
            .FirstOrDefaultAsync(cancellationToken);

        if (chunk is not null)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            try
            {
                var fragment = await claude.ExtractEntities(chunk.Text, cancellationToken);
                chunk.PartialBibleJson = JsonSerializer.Serialize(fragment, JsonResponse.CaseInsensitive);
                chunk.AnalysisStatus = WorkStatus.Completed;
                chunk.LastError = null;
                logger.LogInformation("Analysed chunk {Ordinal} for the design bible (manuscript {NovelId})", chunk.Ordinal, novel.Id);
            }
            // Any transient failure — rate limit, HTTP error, or a network-layer drop like "Socket
            // closed" (an IOException/SocketException, not an HttpRequestException) — is retried with
            // backoff rather than escaping and failing the whole worker cycle.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ScheduleRetry(chunk, ex, novel);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // A chunk whose extraction has permanently FAILED (e.g. a reply that kept truncating at the output
        // ceiling) must not wedge the whole book: its entities are simply absent from the bible, exactly as
        // a permanently-failed reference sheet is tolerated below. Only chunks still awaiting a retry hold
        // us here; once none remain Pending/NeedsRetry, consolidate with whatever chunks succeeded.
        var pendingChunkRetries = await db.Chunks
            .Where(c => c.NovelId == novel.Id && c.AnalysisStatus == WorkStatus.NeedsRetry)
            .Select(c => c.NextAttemptAtUtc)
            .ToListAsync(cancellationToken);
        if (pendingChunkRetries.Count > 0)
            return ProcessResult.Waiting((pendingChunkRetries.Min() ?? now) - now, novel.Id);

        // 2) Every chunk has been analyzed or given up on: consolidate the bible exactly once.
        if (!novel.DesignBibleConsolidated)
        {
            await ConsolidateBible(db, novel, cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 3) Render one pending/due reference sheet. The sheets keep recurring characters/settings
        // consistent across the per-scene evidence images (coherence is part of the fidelity check).
        // NeedsSheet excludes the one-off entities: they are part of the bible but were never going to be
        // drawn, so they are not part of this phase at all — not queued, not counted, and crucially not
        // waited on, since a ref left Pending forever would pin the stage.
        var reference = await db.DesignRefs
            .Where(r => r.NovelId == novel.Id && r.NeedsSheet &&
                        (r.ReferenceSheetStatus == WorkStatus.Pending ||
                         (r.ReferenceSheetStatus == WorkStatus.NeedsRetry && r.NextAttemptAtUtc <= now)))
            .FirstOrDefaultAsync(cancellationToken);

        if (reference is not null)
        {
            if (!geminiLimiter.TryAcquire())
                return ProcessResult.Waiting(geminiLimiter.TimeUntilNext(), novel.Id);

            try
            {
                // Fold the entity's approximate scale into the description so the sheet's scale reference
                // is drawn to the right size (the sheet prompt always asks for a scale reference).
                var described = string.IsNullOrWhiteSpace(reference.Scale)
                    ? reference.CanonicalDescription
                    : $"{reference.CanonicalDescription} Approximate scale: {reference.Scale}.";
                var image = await gemini.GenerateReferenceSheet(described, novel.ArtStyle, cancellationToken);
                reference.ReferenceImagePath = imageStore.Save($"{novel.Id}/refs/{reference.Id}.png", image);
                reference.ReferenceSheetStatus = WorkStatus.Completed;
                reference.LastError = null;
                logger.LogInformation("Rendered reference sheet for {Kind} '{Name}' (manuscript {NovelId})", reference.Kind, reference.Name, novel.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any failure — a rate limit, an HTTP error, or a response that carried no usable image (a
                // text-only or safety-blocked reply, which surfaces as a plain exception) — is retried with
                // backoff and, once non-transient attempts run out, marked Failed. Letting it escape here
                // would crash the whole drain cycle and re-attempt the same sheet forever.
                ScheduleRetry(reference, ex, novel);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // Reference sheets are optional consistency aids — scenes render with whatever sheets exist —
        // so a permanently-failed sheet must not block the stage. Only sheets still awaiting a retry
        // hold us here; once none remain Pending/NeedsRetry, advance even if some ended up Failed.
        var pendingSheetRetries = await db.DesignRefs
            .Where(r => r.NovelId == novel.Id && r.NeedsSheet && r.ReferenceSheetStatus == WorkStatus.NeedsRetry)
            .Select(r => r.NextAttemptAtUtc)
            .ToListAsync(cancellationToken);
        if (pendingSheetRetries.Count > 0)
            return ProcessResult.Waiting((pendingSheetRetries.Min() ?? now) - now, novel.Id);

        // 4) Everything ready: advance to rendering the per-scene evidence images.
        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, SourceIngested: false, AllChunksAnalyzed: true, BibleConsolidated: true,
            AllReferenceSheetsRendered: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task ConsolidateBible(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var fragmentsJson = await db.Chunks
            .Where(c => c.NovelId == novel.Id && c.PartialBibleJson != null)
            .OrderBy(c => c.Ordinal)
            .Select(c => c.PartialBibleJson!)
            .ToListAsync(cancellationToken);

        var fragments = fragmentsJson
            .Select(json => JsonSerializer.Deserialize<DesignBibleFragment>(json, JsonResponse.CaseInsensitive) ?? DesignBibleFragment.Empty)
            .ToList();

        var bible = merger.Merge(fragments);

        // "Recurring" is measured in chunks, so it only means anything once there are at least two of
        // them: in a manuscript short enough to be a single chunk, every entity appears in exactly one
        // chunk and a strict rule would draw no sheets at all, losing consistency across the whole book.
        // With no recurrence signal to go on, draw everything.
        var chunkCount = fragments.Count;

        foreach (var character in bible.Characters)
            db.DesignRefs.Add(NewDesignRef(novel.Id, ReferenceKind.Character, character, chunkCount));
        foreach (var setting in bible.Settings)
            db.DesignRefs.Add(NewDesignRef(novel.Id, ReferenceKind.Setting, setting, chunkCount));

        // Derive the art style from the manuscript (owner's decision: Claude derives it from prose +
        // genre), but only when the user didn't pick one — a custom style always wins. Best-effort and
        // never fatal: any failure keeps the default, since art direction must not stall the review.
        if (novel.ArtStyle == ArtStyles.Default)
        {
            try
            {
                var sample = await db.Chunks
                    .Where(c => c.NovelId == novel.Id)
                    .OrderBy(c => c.Ordinal)
                    .Select(c => c.Text)
                    .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
                if (sample.Length > ArtStyleSampleChars)
                    sample = sample[..ArtStyleSampleChars];

                var derived = await claude.DeriveArtStyle(sample, bible, cancellationToken);
                if (!string.IsNullOrWhiteSpace(derived))
                    novel.ArtStyle = derived.Trim();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Art-style derivation failed; keeping the default style (manuscript {NovelId})", novel.Id);
            }
        }

        novel.DesignBibleConsolidated = true;
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Renders one wordless evidence image per scene. Scene rows are created by scene detection (wired in a
    // following step); until scenes exist this stage has nothing to do and advances straight to the report,
    // so the report composes with no per-scene images (a valid, if evidence-light, review).
    private async Task<ProcessResult> RenderSceneImages(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        if (await PendingWork(db.Scenes.Where(s => s.NovelId == novel.Id), s => s.ImageStatus, s => s.NextAttemptAtUtc, now)
                is { } wait)
            return wait == TimeSpan.MinValue ? ProcessResult.Idle : ProcessResult.Waiting(wait, novel.Id);

        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, SourceIngested: false, AllChunksAnalyzed: false, BibleConsolidated: false,
            AllReferenceSheetsRendered: false, AllSceneImagesRendered: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task<ProcessResult> ComposeAnalysis(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // 0) The report PDF is already on disk, so this stage is finished: advance and do nothing else.
        // This has to come FIRST, ahead of the labelling and seeding below, because none of that
        // preparatory work can change a report that is already written.
        var rendered = await db.BookAnalyses
            .AnyAsync(a => a.NovelId == novel.Id && a.PdfStatus == WorkStatus.Completed, cancellationToken);
        if (rendered)
            return await AdvancePastAnalysis(db, novel, cancellationToken);

        // 0a) Every finding in the report cites a chapter, which the map step reads off its chunk. A
        // conversion chunked before chapter detection existed still has unlabelled chunks, so label
        // them here rather than producing a report with no citations. A no-op once settled.
        if (await EnsureChaptersLabelled(db, novel, fullText: null, cancellationToken))
            return ProcessResult.DidWork(novel.Id);

        // 0b) Seed one analysis unit per chunk (once), plus the per-manuscript report record.
        if (!await db.ChunkAnalyses.AnyAsync(a => a.NovelId == novel.Id, cancellationToken))
        {
            var ordinals = await db.Chunks
                .Where(c => c.NovelId == novel.Id)
                .OrderBy(c => c.Ordinal)
                .Select(c => c.Ordinal)
                .ToListAsync(cancellationToken);
            foreach (var ordinal in ordinals)
                db.ChunkAnalyses.Add(new ChunkAnalysis { NovelId = novel.Id, Ordinal = ordinal });

            if (!await db.BookAnalyses.AnyAsync(a => a.NovelId == novel.Id, cancellationToken))
                db.BookAnalyses.Add(new BookAnalysis { NovelId = novel.Id });

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 1) Map: analyze one pending/due chunk into notes.
        var unit = await db.ChunkAnalyses
            .Where(a => a.NovelId == novel.Id &&
                        (a.Status == WorkStatus.Pending ||
                         (a.Status == WorkStatus.NeedsRetry && a.NextAttemptAtUtc <= now)))
            .OrderBy(a => a.Ordinal)
            .FirstOrDefaultAsync(cancellationToken);

        if (unit is not null)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var chunk = await db.Chunks
                .Where(c => c.NovelId == novel.Id && c.Ordinal == unit.Ordinal)
                .Select(c => new { c.Text, c.ChapterLabel })
                .FirstAsync(cancellationToken);
            var castNames = await db.DesignRefs
                .Where(r => r.NovelId == novel.Id && r.Kind == ReferenceKind.Character)
                .Select(r => r.Name)
                .ToListAsync(cancellationToken);

            try
            {
                var notes = await claude.AnalyzeChapter(
                    chunk.Text, chunk.ChapterLabel, castNames, cancellationToken);
                unit.NotesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
                unit.Status = WorkStatus.Completed;
                unit.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ScheduleRetry(unit, ex, novel);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (await PendingWork(db.ChunkAnalyses.Where(a => a.NovelId == novel.Id), a => a.Status, a => a.NextAttemptAtUtc, now)
                is { } mapWait)
            return mapWait == TimeSpan.MinValue ? ProcessResult.Idle : ProcessResult.Waiting(mapWait, novel.Id);

        var analysis = await db.BookAnalyses.FirstAsync(a => a.NovelId == novel.Id, cancellationToken);

        // 2) Reduce, story half: consolidate all chunk notes into the story report (one Claude call).
        if (analysis.ConsolidatedJson is null)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var notes = await LoadChapterNotes(db, novel.Id, cancellationToken);
            var bible = await LoadBible(db, novel.Id, cancellationToken);

            try
            {
                var report = await claude.ConsolidateAnalysis(novel.Title, notes, bible, novel.ContinuitySummary, cancellationToken);
                analysis.ConsolidatedJson = JsonSerializer.Serialize(report, JsonResponse.CaseInsensitive);
                novel.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2b) Reduce, craft half: line edits, prose style & voice, dialogue, and the scene-by-scene
        // map. Its own call with its own output budget, and persisted separately: asking for both
        // halves at once needs more output than one reply can carry, and a failure in either half
        // must not re-bill the one that already succeeded.
        if (analysis.CraftJson is null)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var notes = await LoadChapterNotes(db, novel.Id, cancellationToken);

            try
            {
                var craft = await claude.ConsolidateCraft(novel.Title, notes, cancellationToken);
                analysis.CraftJson = JsonSerializer.Serialize(craft, JsonResponse.CaseInsensitive);
                novel.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2c) Assess authorial-voice consistency: where the author's own prose drifts from the book's
        // dominant voice. Its own resumable step — its own call, its own column, billed separately — and
        // it needs per-chapter prose SAMPLES rather than the notes, since register/cadence can only be
        // judged from the actual sentences.
        if (analysis.VoiceJson is null)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var samples = await GatherChapterSamples(db, novel.Id, cancellationToken);

            try
            {
                var voice = await claude.AssessVoiceConsistency(samples, cancellationToken);
                analysis.VoiceJson = JsonSerializer.Serialize(voice, JsonResponse.CaseInsensitive);
                novel.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 3) Render: compose the report PDF from the consolidated report (all parts folded into one).
        if (analysis.PdfStatus != WorkStatus.Completed)
        {
            try
            {
                var report = JsonSerializer.Deserialize<BookAnalysisResult>(analysis.ConsolidatedJson, JsonResponse.CaseInsensitive)
                             ?? BookAnalysisResult.Empty;
                var craft = analysis.CraftJson is null
                    ? CraftReport.Empty
                    : JsonSerializer.Deserialize<CraftReport>(analysis.CraftJson, JsonResponse.CaseInsensitive) ?? CraftReport.Empty;
                var voice = analysis.VoiceJson is null
                    ? VoiceConsistency.Empty
                    : JsonSerializer.Deserialize<VoiceConsistency>(analysis.VoiceJson, JsonResponse.CaseInsensitive) ?? VoiceConsistency.Empty;
                var pdf = analysisPdfComposer.Compose(
                    report.WithCraft(craft).WithVoiceConsistency(voice), novel.Title);
                analysis.PdfPath = imageStore.Save($"{novel.Id}/analysis/book-analysis.pdf", pdf);
                analysis.PdfStatus = WorkStatus.Completed;
            }
            // A failed render is left for the next cycle to retry; don't advance on it.
            catch (Exception ex)
            {
                analysis.PdfStatus = WorkStatus.Failed;
                logger.LogError(ex, "Failed to compose the report PDF for manuscript {NovelId}", novel.Id);
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }
        }

        // 4) The report is rendered — either just now or on an earlier cycle — so the review is finished.
        return await AdvancePastAnalysis(db, novel, cancellationToken);
    }

    // Copies the finished report to a Drive folder beside the source manuscript, then optionally frees the
    // local copy. The governing rule is that this stage cannot stop a review from finishing: the report is
    // already on disk and the archive is a convenience. So it advances straight through when archiving is
    // off, when Drive has nowhere sensible to put the file, and when uploads have exhausted their retries.
    private async Task<ProcessResult> ArchiveOutputs(
        StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        if (!archive.Enabled || driveArchive is null)
            return await FinishArchiving(db, novel, cancellationToken);

        var analysis = await db.BookAnalyses
            .FirstOrDefaultAsync(a => a.NovelId == novel.Id, cancellationToken);
        var reportPending = analysis is { PdfStatus: WorkStatus.Completed, PdfPath: not null, ArchivedDriveFileId: null };

        if (!reportPending)
            return await FinishArchiving(db, novel, cancellationToken);

        try
        {
            // Resolve (and remember) the destination once. A manuscript whose source has no resolvable
            // parent — a shared file, or one at the root of a shared drive — has nowhere sensible to
            // archive to, which is a reason to skip rather than to fail.
            if (novel.ArchiveFolderId is null)
            {
                var parent = await driveArchive.FindParentFolder(novel.GoogleDocId, cancellationToken);
                if (parent is null)
                {
                    logger.LogInformation(
                        "Manuscript {NovelId}: the source document has no resolvable Drive folder, so there is " +
                        "nowhere to archive to; finishing without archiving", novel.Id);
                    return await FinishArchiving(db, novel, cancellationToken);
                }

                novel.ArchiveFolderId = await driveArchive.EnsureFolder(parent, novel.Title, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }

            analysis!.ArchivedDriveFileId = await driveArchive.UploadFile(
                novel.ArchiveFolderId, $"{novel.Title} — Draft {novel.DraftNumber} — Analysis.pdf", "application/pdf",
                imageStore.Read(analysis.PdfPath!), cancellationToken);
            logger.LogInformation("Archived the revision report of manuscript {NovelId} to Drive", novel.Id);

            novel.StageAttempts = 0;
            novel.LastError = null;
            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Reuses the manuscript-level attempt counter, as the report consolidations do, since archiving
            // is also a once-per-manuscript step with no per-unit row to hang a budget off. Past the budget
            // the review finishes anyway with the reason recorded — the report is safe on disk regardless.
            novel.StageAttempts++;
            novel.LastError = $"Could not archive to Drive: {ex.Message}";
            if (novel.StageAttempts >= rateLimits.MaxAttempts)
            {
                logger.LogWarning(ex,
                    "Manuscript {NovelId}: giving up on archiving after {Attempts} attempts; finishing anyway " +
                    "(the report remains on the device)", novel.Id, novel.StageAttempts);
                return await FinishArchiving(db, novel, cancellationToken);
            }

            var delay = backoff.NextDelay(novel.StageAttempts - 1, (ex as RateLimitException)?.RetryAfter);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.Waiting(delay, novel.Id);
        }
    }

    // Leaves the archive stage: stamps when it happened (only when the report was actually uploaded),
    // optionally reclaims the local copy, and advances.
    private async Task<ProcessResult> FinishArchiving(
        StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var uploaded = novel.ArchiveFolderId is not null && await db.BookAnalyses
            .AnyAsync(a => a.NovelId == novel.Id && a.ArchivedDriveFileId != null, cancellationToken);
        if (uploaded)
        {
            novel.ArchivedAtUtc = clock.UtcNow;
            if (archive.FreeLocalCopies)
            {
                imageStore.Delete($"{novel.Id}/scenes");
                logger.LogInformation(
                    "Freed the intermediate scene images of manuscript {NovelId} after archiving", novel.Id);
            }
        }

        novel.StageAttempts = 0;
        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, SourceIngested: false, AllChunksAnalyzed: false, BibleConsolidated: false,
            AllReferenceSheetsRendered: false, AllSceneImagesRendered: false, AnalysisComplete: false,
            ArchiveComplete: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // Moves a manuscript off ComposingAnalysis now that its revision report exists. Shared by the two paths
    // that get there: a report rendered on this cycle, and one that was already on disk when the stage
    // was entered (step 0).
    private async Task<ProcessResult> AdvancePastAnalysis(
        StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, SourceIngested: false, AllChunksAnalyzed: false, BibleConsolidated: false,
            AllReferenceSheetsRendered: false, AllSceneImagesRendered: false, AnalysisComplete: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // --- helpers ---

    // Records a failed once-per-manuscript Claude call (a report consolidation) and parks the manuscript
    // for a backoff window instead of letting the failure escape and fail the whole worker cycle. There is
    // no per-unit attempt counter to hang the schedule off here — the unit IS the manuscript — so the delay
    // is the base one, or whatever the provider asked us to wait.
    private async Task<ProcessResult> DeferAfterFailedCall(
        StoryboarderDbContext db, Novel novel, Exception exception, CancellationToken cancellationToken)
    {
        novel.LastError = exception.Message;
        var retryAfter = (exception as RateLimitException)?.RetryAfter;
        var delay = backoff.NextDelay(0, retryAfter);
        if (exception is RateLimitException)
            novel.RateLimitedUntilUtc = clock.UtcNow + delay;
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.Waiting(delay, novel.Id);
    }

    // Detects the manuscript's chapters exactly once (guarded by Novel.ChaptersDetected) and stamps
    // every not-yet-labelled chunk with the chapters its [StartOffset, EndOffset) overlaps, so a
    // finding drawn from that chunk can cite where in the book it came from. Idempotent and cheap once
    // settled. Returns whether anything changed (the caller treats that as one unit of work).
    private async Task<bool> EnsureChaptersLabelled(
        StoryboarderDbContext db, Novel novel, string? fullText, CancellationToken cancellationToken)
    {
        var unlabelled = await db.Chunks
            .Where(c => c.NovelId == novel.Id && c.ChapterLabel == "")
            .ToListAsync(cancellationToken);
        if (novel.ChaptersDetected && unlabelled.Count == 0)
            return false;

        fullText ??= await db.SourceTexts
            .Where(s => s.NovelId == novel.Id)
            .Select(s => s.FullText)
            .FirstOrDefaultAsync(cancellationToken);
        if (fullText is null)
            return false;

        IReadOnlyList<ChapterSpan> spans;
        if (novel.ChaptersDetected)
        {
            spans = (await db.Chapters
                    .Where(c => c.NovelId == novel.Id)
                    .OrderBy(c => c.Number)
                    .ToListAsync(cancellationToken))
                .Select(c => new ChapterSpan(c.Number, c.Label, c.StartOffset, c.EndOffset))
                .ToList();
        }
        else
        {
            spans = chapterDetector.Detect(fullText);
            foreach (var span in spans)
                db.Chapters.Add(new Chapter
                {
                    NovelId = novel.Id,
                    Number = span.Number,
                    Label = span.Label,
                    StartOffset = span.StartOffset,
                    EndOffset = span.EndOffset
                });
            novel.ChaptersDetected = true;
            logger.LogInformation(
                "Detected {Count} chapter(s) in manuscript {NovelId}", spans.Count, novel.Id);
        }

        // No spans means the chapter rows are gone under a manuscript already marked as detected — nothing
        // to attribute to, and re-detecting would contradict the stored (already cited) chapters.
        if (spans.Count == 0)
            return false;

        foreach (var chunk in unlabelled)
        {
            var (number, label) = ChapterDetector.Locate(spans, chunk.StartOffset, chunk.EndOffset);
            chunk.ChapterNumber = number;
            chunk.ChapterLabel = label;
        }

        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Returns null when the phase's units are all complete; otherwise a wait: a positive duration to
    // wait for the soonest retry, or TimeSpan.MinValue meaning "blocked" (only failed units remain).
    private static async Task<TimeSpan?> PendingWork<T>(
        IQueryable<T> units, Func<T, WorkStatus> status, Func<T, DateTimeOffset?> nextAttempt, DateTimeOffset now)
        where T : class
    {
        var all = await units.ToListAsync();
        var outstanding = all.Where(u => status(u) != WorkStatus.Completed).ToList();
        if (outstanding.Count == 0)
            return null;

        var retries = outstanding.Where(u => status(u) == WorkStatus.NeedsRetry).Select(nextAttempt).ToList();
        if (retries.Count == 0)
            return TimeSpan.MinValue; // only failed units remain -> blocked

        var soonest = retries.Min() ?? now;
        return soonest - now;
    }

    private void ScheduleRetry(IRetryable unit, Exception exception, Novel novel)
    {
        unit.Attempts++;
        unit.LastError = exception.Message;
        var rateLimit = exception as RateLimitException;
        var transient = rateLimit is not null;

        if (!transient && unit.Attempts >= rateLimits.MaxAttempts)
        {
            unit.WorkState = WorkStatus.Failed;
            logger.LogWarning("{Unit} failed after {Attempts} attempts and is given up on: {Error}",
                unit.GetType().Name, unit.Attempts, exception.Message);
            return;
        }

        unit.WorkState = WorkStatus.NeedsRetry;

        // Depleted billing credits won't come back on the normal backoff — retrying in seconds just burns
        // calls until the user tops up. Defer a long, flat interval and don't advertise a resume time we
        // can't predict; the error message (which names billing) is what tells the user how to unblock it.
        if (rateLimit?.Kind == RateLimitKind.QuotaExhausted)
        {
            unit.NextAttemptAtUtc = clock.UtcNow + TimeSpan.FromSeconds(rateLimits.MaxDelaySeconds);
            logger.LogWarning("{Unit} deferred: provider quota/credits exhausted; next check {NextAttempt:u} (add billing to resume): {Error}",
                unit.GetType().Name, unit.NextAttemptAtUtc, exception.Message);
            return;
        }

        unit.NextAttemptAtUtc = clock.UtcNow + backoff.NextDelay(unit.Attempts - 1, rateLimit?.RetryAfter);
        // A provider rate limit is the "when do queries resume" the user wants to see, so record the
        // expected resume time on the manuscript; a plain error retry doesn't (it's not a quota wait).
        if (transient)
            novel.RateLimitedUntilUtc = unit.NextAttemptAtUtc;
        logger.LogWarning("{Unit} attempt {Attempts}/{Max} failed ({Kind}); retrying after {NextAttempt:u}: {Error}",
            unit.GetType().Name, unit.Attempts, rateLimits.MaxAttempts,
            transient ? "rate limit" : "error", unit.NextAttemptAtUtc, exception.Message);
    }

    private async Task<DesignBible> LoadBible(StoryboarderDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var refs = await db.DesignRefs.Where(r => r.NovelId == novelId).ToListAsync(cancellationToken);
        EntityProfile Map(DesignRef r) =>
            new(r.Name, r.CanonicalDescription, r.Scale,
                JsonSerializer.Deserialize<List<string>>(r.AliasesJson) ?? [], r.ChunkMentions);
        return new DesignBible(
            refs.Where(r => r.Kind == ReferenceKind.Character).Select(Map).ToList(),
            refs.Where(r => r.Kind == ReferenceKind.Setting).Select(Map).ToList());
    }

    private async Task<IReadOnlyList<ChapterNotes>> LoadChapterNotes(
        StoryboarderDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var notesJson = await db.ChunkAnalyses
            .Where(a => a.NovelId == novelId && a.NotesJson != null)
            .OrderBy(a => a.Ordinal)
            .Select(a => a.NotesJson!)
            .ToListAsync(cancellationToken);

        return notesJson
            .Select(json => JsonSerializer.Deserialize<ChapterNotes>(json, JsonResponse.CaseInsensitive) ?? ChapterNotes.Empty)
            .ToList();
    }

    // Pulls one representative opening prose sample from each detected chapter, for the voice-consistency
    // read. Uses the source text + chapter spans directly (not the notes) because that read judges the
    // actual sentences. A chapter's heading line is skipped so the sample is prose, not the title.
    private async Task<IReadOnlyList<ChapterSample>> GatherChapterSamples(
        StoryboarderDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var fullText = await db.SourceTexts
            .Where(s => s.NovelId == novelId)
            .Select(s => s.FullText)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(fullText))
            return Array.Empty<ChapterSample>();

        var chapters = await db.Chapters
            .Where(c => c.NovelId == novelId)
            .OrderBy(c => c.Number)
            .ToListAsync(cancellationToken);

        var samples = new List<ChapterSample>();
        foreach (var chapter in chapters)
        {
            var start = Math.Clamp(chapter.StartOffset, 0, fullText.Length);
            var end = Math.Clamp(chapter.EndOffset, start, fullText.Length);
            var excerpt = SampleExcerpt(fullText[start..end]);
            if (!string.IsNullOrWhiteSpace(excerpt))
                samples.Add(new ChapterSample(chapter.Label, excerpt));
        }
        return samples;
    }

    // Takes the opening prose of a chapter span: drop a leading heading line, then up to
    // VoiceConsistencySampleChars cut at a word boundary.
    private static string SampleExcerpt(string span)
    {
        var text = span.TrimStart();
        var firstBreak = text.IndexOf('\n');
        // A short first line (a "Chapter 7"-style heading) is dropped; a long one is already prose.
        if (firstBreak >= 0 && firstBreak <= 60)
            text = text[(firstBreak + 1)..].TrimStart();

        text = text.Trim();
        if (text.Length <= VoiceConsistencySampleChars)
            return text;

        var cut = text.LastIndexOf(' ', VoiceConsistencySampleChars - 1);
        return (cut > 0 ? text[..cut] : text[..VoiceConsistencySampleChars]).TrimEnd() + "…";
    }

    private static DesignRef NewDesignRef(
        Guid novelId, ReferenceKind kind, EntityProfile profile, int chunkCount) => new()
    {
        NovelId = novelId,
        Kind = kind,
        Name = profile.Name,
        CanonicalDescription = profile.CanonicalDescription,
        Scale = profile.Scale,
        AliasesJson = JsonSerializer.Serialize(profile.Aliases),
        ChunkMentions = profile.ChunkMentions,
        // Only recurring entities earn a rendered sheet. A one-off entity still reaches the scene-image
        // prompts through the design bible — it just isn't drawn on its own. A single-chunk manuscript
        // carries no recurrence signal at all, so everything is drawn there.
        NeedsSheet = profile.ChunkMentions > 1 || chunkCount <= 1
    };

    private ProcessResult Fail(StoryboarderDbContext db, Novel novel, string message, CancellationToken cancellationToken)
    {
        novel.Stage = ProjectStage.Failed;
        novel.LastError = message;
        Touch(novel);
        db.SaveChanges();
        logger.LogError("Manuscript {NovelId} failed: {Message}", novel.Id, message);
        return ProcessResult.Idle;
    }

    private void SetStage(Novel novel, ProjectStage stage)
    {
        if (novel.Stage != stage)
            logger.LogInformation("Manuscript {NovelId} ({Title}): {From} -> {To}", novel.Id, novel.Title, novel.Stage, stage);
        novel.Stage = stage;
        Touch(novel);
    }

    private void Touch(Novel novel) => novel.UpdatedAtUtc = clock.UtcNow;
}
