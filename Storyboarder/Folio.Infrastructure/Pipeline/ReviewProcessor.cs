using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Folio.Domain.Analysis;
using Folio.Domain.Reading;
using Folio.Domain.Scenes;
using Folio.Domain.Series;
using Folio.Domain.External;
using Folio.Domain.Imaging;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Domain.Model;
using Folio.Domain.Pipeline;
using Storyworthtelling.Voice.Health;
using Folio.Domain.Text;
using Folio.Domain.Time;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.External;
using Folio.Infrastructure.Persistence;
using Storyworthtelling.Paper;
using Storyworthtelling.Remarkable;
using Storyworthtelling.Voice;
using Storyworthtelling.Pipeline;

namespace Folio.Infrastructure.Pipeline;

// The heart of the app: advances one manuscript by one unit of work per call, idempotently and
// resumably. State lives entirely in the database, so the worker resumes after pauses, rate limits, or
// restarts simply by querying the next due unit. A single worker calls ProcessNextAsync in a loop. The
// pipeline maps → merges the design bible, renders one wordless evidence image per scene, map/reduces the
// revision report, composes the report PDF, then archives it beside the source manuscript.
public sealed class ReviewProcessor : IPipelineStep
{
    private readonly IDbContextFactory<FolioDbContext> dbContextFactory;
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

    // The unified delivery seam (Storyworthtelling.Paper): the reMarkable root upload and the Drive
    // beside-source archive behind one interface, each independently available. Null on a head that wires no
    // delivery (or any non-Android host); the archive stage then treats both targets as switched off and the
    // report still finishes on disk. Capability flags say which transports are actually wired.
    private readonly IPaperDelivery? delivery;

    // The owner's-voice learner + shared profile store. Both null on a head that wires no voice feature
    // (or on any non-Android host without a Claude key for it); voice refinement then simply doesn't run.
    private readonly IVoiceClient? voiceClient;
    private readonly IVoiceProfileStore? voiceStore;

    private readonly TextChunker chunker = new();
    private readonly ChapterDetector chapterDetector = new();
    private readonly SceneDetector sceneDetector = new();
    private readonly SceneMatcher sceneMatcher = new();
    private readonly FindingReconciler findingReconciler = new();

    // How many different moments of a scene the model is asked to illustrate before the image is left
    // unavailable. A mature scene may be refused; the report is never blocked over it.
    private const int MaxSceneImageAttempts = 5;

    // Evidence images are portrait (they sit in a column beside the prose in the report).
    private const double PortraitAspectRatio = 3.0 / 4;

    // At most this many reference sheets are attached to one scene render, to keep the request small.
    private const int MaxSceneReferenceSheets = 6;

    // How much opening prose the art-style derivation sees — enough to read the genre, voice and setting
    // without paying for the whole book on a one-off, best-effort call.
    private const int ArtStyleSampleChars = 6000;

    // How much of each chapter the voice-consistency read samples. One representative opening passage per
    // chapter is enough to characterise that chapter's register and cadence and to catch a chapter that
    // drifts from the book's voice, while keeping the whole book's samples inside a single Claude call.
    private const int VoiceConsistencySampleChars = 800;
    private readonly DesignBibleMerger merger = new();

    private readonly IOptions<ArchiveOptions> archiveOptions;

    public ReviewProcessor(
        IDbContextFactory<FolioDbContext> dbContextFactory,
        IClaudeAnalysisClient claude,
        IGeminiImageClient gemini,
        IImageStore imageStore,
        IAnalysisPdfComposer analysisPdfComposer,
        IClock clock,
        IOptions<RateLimitOptions> rateLimitOptions,
        IOptions<ArchiveOptions> archiveOptions,
        ILogger<ReviewProcessor> logger,
        IPaperDelivery? delivery = null,
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
        this.delivery = delivery;
        this.archiveOptions = archiveOptions;

        rateLimits = rateLimitOptions.Value;
        archive = archiveOptions.Value;
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
                        n.Stage != ProjectStage.Cancelled && n.DeletedAtUtc == null)
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

    private async Task<ProcessResult> Dispatch(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        // Release R: a book being regenerated waits for every book before it in its series' story order, so
        // each bible is rebuilt before the next book is read against it; then it is reset, once.
        if (novel.Regenerating)
        {
            if (novel.SeriesId is { } series && await db.Novels.AnyAsync(other =>
                    other.SeriesId == series && other.Regenerating && other.Id != novel.Id
                    && other.RegenerationRank < novel.RegenerationRank && other.DeletedAtUtc == null, cancellationToken))
                return ProcessResult.Idle;
            if (!novel.RegenerationReset)
                return await ResetForRegeneration(db, novel, cancellationToken);
        }

        return await DispatchStage(db, novel, cancellationToken);
    }

    private Task<ProcessResult> DispatchStage(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        // A wait stored on the manuscript (an offline phone, a backed-off call) holds every stage that talks
        // to a provider, not just the step that set it, so a book that cannot reach the network is not
        // walked through the same failing request once per unit.
        if (novel.Stage is ProjectStage.BuildingDesignBible or ProjectStage.RenderingSceneImages
                or ProjectStage.ComposingAnalysis or ProjectStage.Archiving &&
            novel.RetryNotBeforeUtc is { } notBefore && notBefore > clock.UtcNow)
            return Task.FromResult(ProcessResult.Waiting(notBefore - clock.UtcNow, novel.Id));

        return novel.Stage switch
        {
            ProjectStage.Created => Advance(db, novel, cancellationToken),
            ProjectStage.Ingesting => Ingest(db, novel, cancellationToken),
            ProjectStage.BuildingDesignBible => BuildDesignBible(db, novel, cancellationToken),
            ProjectStage.RenderingSceneImages => RenderSceneImages(db, novel, cancellationToken),
            ProjectStage.ComposingAnalysis => ComposeAnalysis(db, novel, cancellationToken),
            ProjectStage.Archiving => ArchiveOutputs(db, novel, cancellationToken),
            _ => Task.FromResult(ProcessResult.Idle)
        };
    }

    // Created -> Ingesting (the only transition with no work of its own).
    private async Task<ProcessResult> Advance(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        SetStage(novel, ProjectStage.Ingesting);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task<ProcessResult> Ingest(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var source = await db.SourceTexts.FirstOrDefaultAsync(s => s.NovelId == novel.Id, cancellationToken);
        if (source is null)
            return Fail(db, novel, "No source text was stored for this manuscript.", cancellationToken);

        if (!await db.Chunks.AnyAsync(c => c.NovelId == novel.Id, cancellationToken))
        {
            // Chunks never span a chapter start, so each analysis unit is one chapter (or a piece of a long
            // one). Detection is deterministic, so the chapter rows written by EnsureChaptersLabelled below
            // are exactly these spans.
            var chapterStarts = chapterDetector.Detect(source.FullText).Select(s => s.StartOffset).ToList();
            foreach (var span in chunker.Chunk(source.FullText, chapterStarts))
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
        await RefineOwnerVoice(db, novel, source.FullText, cancellationToken);

        SetStage(novel, ProjectStage.BuildingDesignBible);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // Folds this draft's manuscript into the owner's shared voice profile, read through a sample spread across the
    // whole book (VoiceSample, audit W10.6). A profile marked to be learned again (a discarded book took its anchors
    // but not what it taught the register, or the profile was in an older shape) is rebuilt instead, from every
    // project's latest draft, this one included, and each of those drafts is marked analysed. No-op when no voice
    // client/store is wired or the draft has already been analysed. A failure is logged and leaves VoiceAnalyzed
    // false so a later ingest can retry — voiced messaging is a nicety, never a gate on a review.
    private async Task RefineOwnerVoice(FolioDbContext db, Novel novel, string manuscript, CancellationToken cancellationToken)
    {
        if (voiceClient is null || voiceStore is null || novel.VoiceAnalyzed)
            return;

        try
        {
            var existing = await voiceStore.LoadAsync(cancellationToken);
            if (existing.NeedsRelearn)
            {
                var drafts = await LatestDrafts(db, novel, cancellationToken);
                var books = drafts.Select(draft => new VoiceSource(
                    draft.ProjectId, draft.Id == novel.Id ? manuscript : draft.Source?.FullText ?? string.Empty))
                    .Where(book => book.Text.Length > 0)
                    .ToList();
                var rebuilt = await VoiceRelearn.RebuildAsync(voiceClient, books, cancellationToken);
                await voiceStore.SaveAsync(rebuilt, cancellationToken);
                foreach (var draft in drafts)
                    draft.VoiceAnalyzed = true;
                novel.VoiceAnalyzed = true;
                logger.LogInformation(
                    "Relearned owner voice from {Books} book(s) at manuscript {NovelId}", books.Count, novel.Id);
                return;
            }

            var refined = await voiceClient.RefineAsync(existing, VoiceSample.Of(manuscript), novel.ProjectId, cancellationToken);
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

    // Each project's latest live draft, oldest project first, with this draft standing for its own project.
    private static async Task<List<Novel>> LatestDrafts(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var all = await db.Novels
            .Where(n => n.DeletedAtUtc == null && n.Stage != ProjectStage.Cancelled)
            .Include(n => n.Source)
            .ToListAsync(cancellationToken);

        return all
            .Where(n => n.ProjectId != novel.ProjectId)
            .GroupBy(n => n.ProjectId)
            .Select(project => project.OrderByDescending(n => n.CreatedAtUtc).First())
            .Append(novel)
            .OrderBy(n => n.CreatedAtUtc)
            .ToList();
    }

    private async Task<ProcessResult> BuildDesignBible(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
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

    private async Task ConsolidateBible(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
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

        var added = new List<DesignRef>(bible.Characters.Count + bible.Settings.Count);
        foreach (var character in bible.Characters)
            added.Add(NewDesignRef(novel.Id, ReferenceKind.Character, character, chunkCount));
        foreach (var setting in bible.Settings)
            added.Add(NewDesignRef(novel.Id, ReferenceKind.Setting, setting, chunkCount));
        db.DesignRefs.AddRange(added);

        // On a sequel, a returning character/place reuses the prior book's rendered reference image so it stays
        // visually on-model across books (the book-level mirror of CarryForwardScenes). Its own description is
        // kept — the character may have evolved, and the continuity read compares the two.
        if (novel.PreviousBookId is { } priorBookId)
            await CarryForwardSeriesEntities(db, novel, priorBookId, added, cancellationToken);

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

    // Detects scenes (once), then renders one wordless evidence image per scene — the moment the prose is
    // measured against. A scene the model refuses even after retrying different moments is left with no
    // image (never blocking the report). Advances when every scene has an image or has given up on one.
    private async Task<ProcessResult> RenderSceneImages(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var source = await db.SourceTexts
            .Where(s => s.NovelId == novel.Id)
            .Select(s => s.FullText)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        // 0) Seed the scenes once, from the detected chapters + source text.
        if (!await db.Scenes.AnyAsync(s => s.NovelId == novel.Id, cancellationToken))
        {
            var chapters = (await db.Chapters
                    .Where(c => c.NovelId == novel.Id)
                    .OrderBy(c => c.Number)
                    .ToListAsync(cancellationToken))
                .Select(c => new ChapterSpan(c.Number, c.Label, c.StartOffset, c.EndOffset))
                .ToList();

            var spans = sceneDetector.Detect(source, chapters);
            var scenes = spans
                .Select(span => new Scene
                {
                    NovelId = novel.Id,
                    Number = span.Number,
                    ChapterNumber = span.ChapterNumber,
                    ChapterLabel = span.ChapterLabel,
                    StartOffset = span.StartOffset,
                    EndOffset = span.EndOffset
                })
                .ToList();

            // On a later draft, match these scenes against the previous draft's so an unchanged scene reuses
            // its rendered image (the render loop then skips it) and a changed scene is marked for a fresh
            // render + a before/after pair. A first draft skips this and renders every scene.
            var reused = novel.PreviousDraftId is { } previousId
                ? await CarryForwardScenes(db, novel, previousId, source, scenes, cancellationToken)
                : 0;

            db.Scenes.AddRange(scenes);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Detected {Count} scenes for manuscript {NovelId} ({Reused} images reused from the previous draft)",
                scenes.Count, novel.Id, reused);
            return ProcessResult.DidWork(novel.Id);
        }

        // 0b) The image prompts go to the batch tier first (owner's ruling, R39): every fresh scene still
        // without a prompt in one batch at half the cost. While it runs, scenes whose prompts are back are
        // rendered; a scene retrying after a refusal (LastError set) always goes one at a time, since it
        // carries a moment to avoid.
        var promptless = await db.Scenes
            .Where(s => s.NovelId == novel.Id && s.ImageStatus == WorkStatus.Pending
                        && (s.ImagePrompt == null || s.ImagePrompt == "") && s.LastError == null)
            .OrderBy(s => s.Number)
            .ToListAsync(cancellationToken);
        var promptBatch = await db.ModelBatches.FirstOrDefaultAsync(
            b => b.NovelId == novel.Id && b.Kind == BatchKinds.SceneImagePrompts, cancellationToken);
        var (promptStep, promptWait) = BatchPlan.Decide(promptless.Count, StateOf(promptBatch), now);
        var promptsInFlight = false;

        if (promptStep == BatchStep.Submit)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var bible = await LoadBible(db, novel.Id, cancellationToken);
            var requests = promptless.Select(s =>
            {
                var start = Math.Clamp(s.StartOffset, 0, source.Length);
                return new SceneImageRequest(s.Number, source[start..Math.Clamp(s.EndOffset, start, source.Length)]);
            }).ToList();
            await SubmitBatch(db, novel, BatchKinds.SceneImagePrompts,
                () => claude.SubmitSceneImagePrompts(requests, bible, novel.ArtStyle, cancellationToken), cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (promptStep == BatchStep.Poll)
        {
            if (await PollBatch(db, promptBatch!, cancellationToken))
            {
                var byNumber = promptless.ToDictionary(s => s.Number);
                var results = await claude.SceneImagePromptResults(promptBatch!.BatchId, cancellationToken);
                foreach (var result in results)
                {
                    if (!string.IsNullOrWhiteSpace(result.Value) && byNumber.TryGetValue(result.SceneNumber, out var prompted))
                        prompted.ImagePrompt = result.Value;
                }
                promptBatch.CompletedAtUtc = now;
                logger.LogInformation(
                    "Image prompt batch {BatchId} for manuscript {NovelId}: {Prompted} of {Count} prompts; the rest go one at a time",
                    promptBatch.BatchId, novel.Id, results.Count(result => !string.IsNullOrWhiteSpace(result.Value)), promptless.Count);
                Touch(novel);
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }
            promptsInFlight = true;
            promptWait = BatchPlan.PollEvery;
        }
        else if (promptStep == BatchStep.Wait)
        {
            promptsInFlight = true;
        }
        else if (promptStep == BatchStep.Abandon)
        {
            await AbandonBatch(db, novel, promptBatch!, cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 0c) The pictures go to Gemini's batch tier (owner's request): the fresh scenes whose prompts are back,
        // in batches within the inline size budget, one batch at a time. While one is out, those scenes wait;
        // scenes still needing a prompt, and retries after a refusal, go on one at a time. A scene a batch did
        // not paint goes one at a time afterwards. A refused batch, or one that comes back with no pictures at
        // all, turns batching off for the draft; an offline phone just waits.
        var imageBatches = await db.ModelBatches
            .Where(b => b.NovelId == novel.Id && b.Kind.StartsWith(BatchKinds.SceneImagesPrefix))
            .ToListAsync(cancellationToken);
        var batchingOff = imageBatches.Any(b => b.Kind == BatchKinds.SceneImagesOff);
        var openImages = imageBatches.FirstOrDefault(b => b.Kind != BatchKinds.SceneImagesOff && b.CompletedAtUtc == null);
        var imagesInFlight = false;
        var imagesWait = BatchPlan.PollEvery;
        if (!batchingOff && !promptsInFlight)
        {
            var fresh = await db.Scenes
                .Where(s => s.NovelId == novel.Id && s.ImageStatus == WorkStatus.Pending && !s.ImageBatched
                            && s.ImagePrompt != null && s.ImagePrompt != "" && s.LastError == null)
                .OrderBy(s => s.Number)
                .ToListAsync(cancellationToken);
            var (imageStep, wait) = BatchPlan.Decide(openImages is null ? fresh.Count : 1, StateOf(openImages), now);
            switch (imageStep)
            {
                case BatchStep.Submit:
                    return await SubmitImageBatch(db, novel, source, fresh, imageBatches, now, cancellationToken);
                case BatchStep.Poll:
                    if (await PollImageBatch(db, novel, openImages!, now, cancellationToken) is { } polled)
                        return polled;
                    imagesInFlight = true;
                    break;
                case BatchStep.Wait:
                    imagesInFlight = true;
                    imagesWait = wait;
                    break;
                case BatchStep.Abandon:
                    openImages!.CompletedAtUtc = now;
                    logger.LogWarning("Image batch {BatchId} for manuscript {NovelId} outlived its window; its scenes go one at a time",
                        openImages.BatchId, novel.Id);
                    await db.SaveChangesAsync(cancellationToken);
                    return ProcessResult.DidWork(novel.Id);
            }
        }

        // 1) Render one pending/due scene image. While a prompt batch is in flight, only a scene whose
        // prompt is already back is taken; with none, wait for the next poll. While a picture batch is in
        // flight, a fresh scene waits for it.
        var scene = await db.Scenes
            .Where(s => s.NovelId == novel.Id &&
                        (s.ImageStatus == WorkStatus.Pending ||
                         (s.ImageStatus == WorkStatus.NeedsRetry && s.NextAttemptAtUtc <= now)) &&
                        (!promptsInFlight || (s.ImagePrompt != null && s.ImagePrompt != "")) &&
                        (!imagesInFlight || s.ImageBatched || s.LastError != null || s.ImagePrompt == null || s.ImagePrompt == ""))
            // With batching on, every prompt is derived before any fresh picture is painted, so the fresh scenes
            // gather for a batch rather than each being painted the moment its own prompt is back.
            .OrderBy(s => batchingOff || (s.ImagePrompt != null && s.ImagePrompt != "") ? 1 : 0)
            .ThenBy(s => s.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (scene is null && promptsInFlight)
            return ProcessResult.Waiting(promptWait, novel.Id);
        if (scene is null && imagesInFlight)
            return ProcessResult.Waiting(imagesWait, novel.Id);

        if (scene is not null)
        {
            var start = Math.Clamp(scene.StartOffset, 0, source.Length);
            var end = Math.Clamp(scene.EndOffset, start, source.Length);
            var sceneText = source[start..end];

            // 1a) Derive the wordless image prompt (a Claude call). On a retry after a refusal, the refused
            // prompt is remembered on LastError and passed as a moment to avoid so a different beat is tried.
            if (string.IsNullOrWhiteSpace(scene.ImagePrompt))
            {
                if (!claudeLimiter.TryAcquire())
                    return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

                try
                {
                    var bible = await LoadBible(db, novel.Id, cancellationToken);
                    var avoid = string.IsNullOrWhiteSpace(scene.LastError) ? Array.Empty<string>() : [scene.LastError];
                    scene.ImagePrompt = await claude.DescribeSceneImage(sceneText, bible, novel.ArtStyle, avoid, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ScheduleRetry(scene, ex, novel);
                    await db.SaveChangesAsync(cancellationToken);
                    return ProcessResult.DidWork(novel.Id);
                }

                // No prompt could be derived: leave the scene without an image rather than blocking.
                if (string.IsNullOrWhiteSpace(scene.ImagePrompt))
                {
                    scene.ImageStatus = WorkStatus.Completed;
                    scene.ImagePath = null;
                    scene.LastError = "Image unavailable: no image prompt could be derived.";
                }
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }

            // 1b) Render the image (a Gemini call), supplying reference sheets for any recurring entity
            // named in the scene so characters/places stay on-model.
            if (!geminiLimiter.TryAcquire())
                return ProcessResult.Waiting(geminiLimiter.TimeUntilNext(), novel.Id);

            try
            {
                var references = await LoadSceneReferenceImages(db, novel.Id, sceneText, cancellationToken);
                var image = await gemini.GenerateSceneImage(
                    scene.ImagePrompt, novel.ArtStyle, references, PortraitAspectRatio, cancellationToken);
                scene.ImagePath = imageStore.Save($"{novel.Id}/scenes/scene-{scene.Number}.png", image);
                scene.ImageStatus = WorkStatus.Completed;
                scene.LastError = null;
            }
            catch (ImageContentBlockedException blocked)
            {
                // The model refused this moment. Retry a DIFFERENT moment (remembering the refused prompt as
                // one to avoid), up to a cap — after which the scene is left without an image, never blocked.
                scene.Attempts++;
                if (scene.Attempts >= MaxSceneImageAttempts)
                {
                    scene.ImageStatus = WorkStatus.Completed;
                    scene.ImagePath = null;
                    scene.LastError = "Image unavailable: the model refused every attempted moment.";
                    logger.LogWarning("Scene {Number} of manuscript {NovelId} left without an image after {Attempts} refusals: {Reason}",
                        scene.Number, novel.Id, scene.Attempts, blocked.Reason);
                }
                else
                {
                    scene.LastError = scene.ImagePrompt;   // fed back as a moment to avoid on the retry
                    scene.ImagePrompt = null;              // re-derive a different beat next cycle
                    scene.NextAttemptAtUtc = null;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ScheduleRetry(scene, ex, novel);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2) No scene is pending/due. If any are waiting on a transient retry, wait for the soonest;
        // otherwise every scene has an image or has given up on one — advance. (A scene left Failed by an
        // exhausted transient retry counts as "image unavailable", not a blocker.)
        var pendingRetries = await db.Scenes
            .Where(s => s.NovelId == novel.Id && s.ImageStatus == WorkStatus.NeedsRetry)
            .Select(s => s.NextAttemptAtUtc)
            .ToListAsync(cancellationToken);
        if (pendingRetries.Count > 0)
            return ProcessResult.Waiting((pendingRetries.Min() ?? now) - now, novel.Id);

        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, SourceIngested: false, AllChunksAnalyzed: false, BibleConsolidated: false,
            AllReferenceSheetsRendered: false, AllSceneImagesRendered: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // Release R: resets a finished draft for its fresh report, in place. Scenes are detected again under the
    // current rules; a scene whose span is unchanged keeps its picture (the text is the same manuscript), and
    // on a later draft the previous draft's pictures are carried as on any new draft. The reads, the round
    // table, the plan and the series checks are cleared to run again; the chapter notes, the design bible, the
    // jacket and the placement are kept, and the voice step is never re-entered, so the profile is untouched.
    private async Task<ProcessResult> ResetForRegeneration(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var source = await Manuscript(db, novel.Id, cancellationToken);
        var oldScenes = await db.Scenes.Where(scene => scene.NovelId == novel.Id).ToListAsync(cancellationToken);
        // The old pictures are read first: the new scenes are saved under the same file names.
        var kept = new Dictionary<(int Start, int End), (byte[] Image, string? Prompt)>();
        foreach (var old in oldScenes.Where(scene => scene.ImagePath is { } path && imageStore.Exists(path)))
            kept.TryAdd((old.StartOffset, old.EndOffset), (imageStore.Read(old.ImagePath!), old.ImagePrompt));

        var chapters = (await db.Chapters.Where(c => c.NovelId == novel.Id).OrderBy(c => c.Number).ToListAsync(cancellationToken))
            .Select(c => new ChapterSpan(c.Number, c.Label, c.StartOffset, c.EndOffset)).ToList();
        var scenes = sceneDetector.Detect(source, chapters).Select(span => new Scene
        {
            NovelId = novel.Id,
            Number = span.Number,
            ChapterNumber = span.ChapterNumber,
            ChapterLabel = span.ChapterLabel,
            StartOffset = span.StartOffset,
            EndOffset = span.EndOffset,
        }).ToList();
        db.Scenes.RemoveRange(oldScenes);
        if (novel.PreviousDraftId is { } previousId)
            await CarryForwardScenes(db, novel, previousId, source, scenes, cancellationToken);

        var reused = 0;
        foreach (var scene in scenes.Where(scene => scene.ImageStatus != WorkStatus.Completed))
        {
            if (!kept.TryGetValue((scene.StartOffset, scene.EndOffset), out var picture))
                continue;
            scene.ImagePath = imageStore.Save($"{novel.Id}/scenes/scene-{scene.Number}.png", picture.Image);
            scene.ImagePrompt = picture.Prompt;
            scene.ImageStatus = WorkStatus.Completed;
            reused++;
        }
        db.Scenes.AddRange(scenes);

        db.PassReads.RemoveRange(db.PassReads.Where(pass => pass.NovelId == novel.Id));
        db.Findings.RemoveRange(db.Findings.Where(finding => finding.NovelId == novel.Id));
        db.ModelBatches.RemoveRange(db.ModelBatches.Where(batch => batch.NovelId == novel.Id));
        if (await db.BookAnalyses.FirstOrDefaultAsync(a => a.NovelId == novel.Id, cancellationToken) is { } analysis)
        {
            analysis.ConsolidatedJson = null;
            analysis.CraftJson = null;
            analysis.VoiceJson = null;
            analysis.ExpansionJson = null;
            analysis.FindingsReconciled = false;
            analysis.SeriesContinuityJson = null;
            analysis.SeriesBibleUpdated = false;
            analysis.SeriesBibleDelivered = false;
            analysis.RoundTableJson = null;
            analysis.PdfStatus = WorkStatus.Pending;
            analysis.ArchivedDriveFileId = null;
        }

        novel.RegenerationReset = true;
        novel.StageAttempts = 0;
        novel.LastError = null;
        SetStage(novel, ProjectStage.RenderingSceneImages);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Reset manuscript {NovelId} for regeneration: {Count} scenes, {Reused} pictures kept from the old report",
            novel.Id, scenes.Count, reused);
        return ProcessResult.DidWork(novel.Id);
    }

    // Matches a later draft's freshly-detected scenes against the previous draft's (by text, via
    // SceneMatcher) and stamps each with its cross-draft identity. An UNCHANGED scene reuses the previous
    // draft's rendered image — copied into this draft's scene folder and marked Completed so the render loop
    // skips it (or mirrored as unavailable when the previous image was itself unavailable). A CHANGED scene
    // keeps its PreviousSceneId (for the before/after pair) but stays Pending so it re-renders. Returns how
    // many images were carried forward. Best-effort: any copy failure just leaves the scene to re-render.
    private async Task<int> CarryForwardScenes(
        FolioDbContext db, Novel novel, Guid previousDraftId, string source,
        IReadOnlyList<Scene> scenes, CancellationToken cancellationToken)
    {
        var previousScenes = await db.Scenes
            .Where(s => s.NovelId == previousDraftId)
            .Select(s => new { s.Id, s.Number, s.StartOffset, s.EndOffset, s.ImagePath, s.ImagePrompt })
            .ToListAsync(cancellationToken);
        if (previousScenes.Count == 0)
            return 0;

        var previousSource = await db.SourceTexts
            .Where(s => s.NovelId == previousDraftId)
            .Select(s => s.FullText)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        static string Slice(string text, int start, int end)
        {
            var from = Math.Clamp(start, 0, text.Length);
            var to = Math.Clamp(end, from, text.Length);
            return text[from..to];
        }

        var previousById = previousScenes.ToDictionary(p => p.Number);
        var previousTexts = previousScenes
            .Select(p => new SceneText(p.Number, Slice(previousSource, p.StartOffset, p.EndOffset)))
            .ToList();
        var currentTexts = scenes
            .Select(s => new SceneText(s.Number, Slice(source, s.StartOffset, s.EndOffset)))
            .ToList();

        var matches = sceneMatcher.Match(previousTexts, currentTexts)
            .ToDictionary(m => m.CurrentNumber);

        var reused = 0;
        foreach (var scene in scenes)
        {
            if (!matches.TryGetValue(scene.Number, out var match) || match.PreviousNumber is not { } prevNumber)
                continue;   // a new scene — no counterpart, renders fresh

            var prior = previousById[prevNumber];
            scene.PreviousSceneId = prior.Id;
            scene.Changed = match.Changed;
            if (match.Changed)
                continue;   // same beat, different words — re-render and pair before/after

            // Unchanged: reuse the previous draft's image so nothing re-renders.
            scene.ImagePrompt = prior.ImagePrompt;
            scene.ImageStatus = WorkStatus.Completed;
            if (prior.ImagePath is { } priorPath && imageStore.Exists(priorPath))
            {
                try
                {
                    scene.ImagePath = imageStore.Save($"{novel.Id}/scenes/scene-{scene.Number}.png", imageStore.Read(priorPath));
                    reused++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The copy failed — fall back to re-rendering this scene rather than blocking.
                    scene.ImageStatus = WorkStatus.Pending;
                    scene.ImagePrompt = null;
                    logger.LogWarning(ex, "Could not carry forward scene {Number} image for manuscript {NovelId}; will re-render",
                        scene.Number, novel.Id);
                }
            }
            else
            {
                // The previous image was unavailable; the unchanged text won't render any better, so mirror it.
                scene.ImagePath = null;
                scene.LastError = "Image unavailable: carried forward from the previous draft.";
            }
        }

        return reused;
    }

    // Reuses the prior book's rendered reference images for this book's returning entities (Phase 5). Matched
    // by name — the merged name, or any alias — against the prior book's design refs, scoped by kind. A match
    // copies the prior image into this book's refs folder and marks the sheet Completed, so the render loop
    // skips it: a returning character/place keeps its established look across books. Best-effort: any copy
    // failure just leaves the entity to render fresh. Only entities that earn a sheet are considered. Returns
    // how many images were carried forward.
    private async Task<int> CarryForwardSeriesEntities(
        FolioDbContext db, Novel novel, Guid priorBookId,
        IReadOnlyList<DesignRef> entities, CancellationToken cancellationToken)
    {
        var priorRefs = await db.DesignRefs
            .Where(r => r.NovelId == priorBookId && r.ReferenceImagePath != null)
            .Select(r => new { r.Kind, r.Name, r.AliasesJson, r.ReferenceImagePath })
            .ToListAsync(cancellationToken);
        if (priorRefs.Count == 0)
            return 0;

        // A (kind, lower-cased name-or-alias) → prior image path lookup, so a rename that kept an alias still matches.
        var priorByName = new Dictionary<(ReferenceKind, string), string>();
        foreach (var prior in priorRefs)
        {
            void Register(string? name)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    priorByName.TryAdd((prior.Kind, name.Trim().ToLowerInvariant()), prior.ReferenceImagePath!);
            }
            Register(prior.Name);
            foreach (var alias in JsonSerializer.Deserialize<List<string>>(prior.AliasesJson) ?? [])
                Register(alias);
        }

        var reused = 0;
        foreach (var entity in entities.Where(e => e.NeedsSheet))
        {
            var names = new[] { entity.Name }
                .Concat(JsonSerializer.Deserialize<List<string>>(entity.AliasesJson) ?? [])
                .Select(n => n?.Trim().ToLowerInvariant())
                .Where(n => !string.IsNullOrEmpty(n));
            string? priorPath = null;
            foreach (var name in names)
                if (priorByName.TryGetValue((entity.Kind, name!), out priorPath))
                    break;
            if (priorPath is null || !imageStore.Exists(priorPath))
                continue;

            try
            {
                entity.ReferenceImagePath = imageStore.Save($"{novel.Id}/refs/{entity.Id}.png", imageStore.Read(priorPath));
                entity.ReferenceSheetStatus = WorkStatus.Completed;
                entity.LastError = null;
                reused++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Could not carry forward the reference image for '{Name}' (manuscript {NovelId}); will render fresh",
                    entity.Name, novel.Id);
            }
        }

        if (reused > 0)
            logger.LogInformation(
                "Carried forward {Reused} reference image(s) from the prior book (manuscript {NovelId})", reused, novel.Id);
        return reused;
    }

    // The rendered reference sheets for recurring entities named in the scene text, capped so one render's
    // payload stays small. Matched by entity name appearing in the prose (a light heuristic — scenes don't
    // carry an explicit cast list).
    private async Task<IReadOnlyList<byte[]>> LoadSceneReferenceImages(
        FolioDbContext db, Guid novelId, string sceneText, CancellationToken cancellationToken)
    {
        var sheets = await db.DesignRefs
            .Where(r => r.NovelId == novelId && r.ReferenceImagePath != null)
            .Select(r => new { r.Name, r.ReferenceImagePath })
            .ToListAsync(cancellationToken);

        return sheets
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) &&
                        sceneText.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
            .Take(MaxSceneReferenceSheets)
            .Select(s => s.ReferenceImagePath!)
            .Where(imageStore.Exists)
            .Select(imageStore.Read)
            .ToList();
    }

    private async Task<ProcessResult> ComposeAnalysis(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
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
                // Thread the running story-state through the map in reading order: each chunk is analyzed
                // with the story-so-far and returns it updated, so continuity and setup/payoff survive the
                // chunk-by-chunk read. The accumulated state feeds the consolidation (and cross-draft
                // reconciliation) as the continuity summary.
                var notes = await claude.AnalyzeChapter(
                    chunk.Text, chunk.ChapterLabel, castNames, novel.ContinuitySummary, cancellationToken);
                unit.NotesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
                unit.Status = WorkStatus.Completed;
                unit.LastError = null;
                if (!string.IsNullOrWhiteSpace(notes.RunningState))
                {
                    novel.ContinuitySummary = notes.RunningState;
                    Touch(novel);
                }
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

        // A reduction or a reading failed earlier: honour the wait it set, or the parking it ended in, before
        // sending the whole book to Claude again.
        if (CallGate(novel, now) is { } gated)
            return gated;

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
                ClearCallFailures(novel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, "ConsolidateAnalysis", cancellationToken);
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
                ClearCallFailures(novel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, "ConsolidateCraft", cancellationToken);
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
                ClearCallFailures(novel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, "AssessVoiceConsistency", cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2d) Reconcile findings across drafts: give this draft's consolidated findings a durable identity by
        // matching them against the prior draft's threads (a Claude call on a later draft; deterministic for a
        // first draft), so a finding can be tracked opened → carried → fixed → regressed. Its own resumable
        // step, gated by FindingsReconciled so the (billed) match runs once even when there are zero findings.
        if (!analysis.FindingsReconciled)
        {
            var report = JsonSerializer.Deserialize<BookAnalysisResult>(analysis.ConsolidatedJson, JsonResponse.CaseInsensitive)
                         ?? BookAnalysisResult.Empty;
            var candidates = ToCandidateFindings(report);

            ReconciliationOutcome outcome;
            if (novel.PreviousDraftId is { } previousDraftId)
            {
                if (!claudeLimiter.TryAcquire())
                    return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

                var priorThreads = await LoadPriorThreads(db, novel.ProjectId, previousDraftId, cancellationToken);
                try
                {
                    var reconciliation = await claude.ReconcileFindings(priorThreads, candidates, cancellationToken);
                    outcome = findingReconciler.Reconcile(
                        novel.Id, novel.ProjectId, novel.DraftNumber, priorThreads, candidates, reconciliation);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return await DeferAfterFailedCall(db, novel, ex, "ReconcileFindings", cancellationToken);
                }
            }
            else
            {
                outcome = findingReconciler.ReconcileFirstDraft(novel.Id, novel.ProjectId, novel.DraftNumber, candidates);
            }

            foreach (var occurrence in outcome.Occurrences)
                occurrence.CreatedAtUtc = now;
            db.Findings.AddRange(outcome.Occurrences);
            analysis.FindingsReconciled = true;
            ClearCallFailures(novel);
            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2e) The additive expansion read (Phase 3): scenes/beats to add, show-vs-tell, length vs genre norm.
        // Its own resumable step, own column, billed once — like the craft/voice halves.
        if (analysis.ExpansionJson is null)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var notes = await LoadChapterNotes(db, novel.Id, cancellationToken);
            var wordCount = await WordCount(db, novel.Id, cancellationToken);
            var genre = JsonSerializer.Deserialize<BookAnalysisResult>(analysis.ConsolidatedJson, JsonResponse.CaseInsensitive)?.Genre
                        ?? string.Empty;

            try
            {
                var expansion = await claude.ProposeExpansion(novel.Title, notes, novel.ContinuitySummary, wordCount, genre, cancellationToken);
                analysis.ExpansionJson = JsonSerializer.Serialize(expansion, JsonResponse.CaseInsensitive);
                ClearCallFailures(novel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, "ProposeExpansion", cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2e1) A phone with no bible for this series (a reset) puts it back from Drive first, so the placement
        // guess and the continuity read see the owner's corrections. Once per draft; a phone that has its bible
        // asks Drive nothing. Best-effort: whatever the outcome, the review goes on.
        if (novel.SeriesId is { } restoreSeries && !analysis.SeriesBibleRestoreChecked)
        {
            var restored = await new SeriesBibleBackup(dbContextFactory, delivery, archiveOptions, clock, logger)
                .RestoreAsync(restoreSeries, cancellationToken);
            if (restored != BibleRestore.NotNeeded)
                logger.LogInformation("Series bible restore for manuscript {NovelId}: {Outcome}", novel.Id, restored);
            analysis.SeriesBibleRestoreChecked = true;
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2e2) Guess where a series book sits in its series' story time (R21), once, when there are other
        // books to place it against. The owner confirms or changes it on the book's page; a confirmed
        // placement is carried onto later drafts and never guessed again.
        if (novel.SeriesId is not null && novel.PlacementJson is null
            && await EarlierBooks(db, novel, cancellationToken) is { Count: > 0 } others)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var bible = SeriesBibleCodec.Read(await db.SeriesBibles
                .Where(record => record.SeriesId == novel.SeriesId)
                .Select(record => record.Json)
                .FirstOrDefaultAsync(cancellationToken));
            try
            {
                var placement = await claude.PlaceInSeries(
                    novel.Title, [.. others.Select(book => book.Title)],
                    BibleText.For(bible.Without(novel.ProjectId)), novel.ContinuitySummary, cancellationToken);
                novel.PlacementJson = StoryPlacementCodec.Write(placement with { Confirmed = false });
                ClearCallFailures(novel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, "PlaceInSeries", cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2f) Cross-book continuity read (Phase 5): only for a book linked to a prior one. Checks this book
        // against the prior book's established canon and tracks its carried-over open threads. Its own resumable
        // step + column, billed once — like the other reduce halves. A standalone book has no PreviousBookId, so
        // it skips this entirely and the column stays null (no series section renders).
        if (novel.PreviousBookId is { } priorBookId && analysis.SeriesContinuityJson is null)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var canon = await LoadSeriesCanon(db, priorBookId, cancellationToken);
            var notes = await LoadChapterNotes(db, novel.Id, cancellationToken);
            var (_, bible) = await LoadSeriesBible(db, novel, cancellationToken);

            try
            {
                var continuity = await claude.AssessSeriesContinuity(
                    novel.Title, canon, BibleText.For(bible.Without(novel.ProjectId)),
                    StoryPlacementCodec.Read(novel.PlacementJson), notes, novel.ContinuitySummary, cancellationToken);
                analysis.SeriesContinuityJson = JsonSerializer.Serialize(continuity, JsonResponse.CaseInsensitive);
                ClearCallFailures(novel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, "AssessSeriesContinuity", cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2f2) Judge each detected scene on its own (release A3–A4): what it accomplishes, keep / tighten /
        // rewrite / cut, its tension and its inconsistencies. One scene per turn, stored on the scene the
        // moment it succeeds. The verdicts used to come from the craft reduce's own list of scenes, zipped
        // against the detected scenes by position, so any difference in count printed every later verdict
        // under the wrong scene.
        var unjudged = await db.Scenes
            .Where(s => s.NovelId == novel.Id && s.JudgementJson == null)
            .OrderBy(s => s.Number)
            .ToListAsync(cancellationToken);
        if (unjudged.Count > 0)
        {
            // The batch tier first (owner's ruling, R39): every unjudged scene in one batch at half the
            // cost. While it runs the reads carry on; the report waits for the last judgement below.
            var batch = await db.ModelBatches.FirstOrDefaultAsync(
                b => b.NovelId == novel.Id && b.Kind == BatchKinds.SceneJudgements, cancellationToken);
            var (step, _) = BatchPlan.Decide(unjudged.Count, StateOf(batch), clock.UtcNow);

            if (step == BatchStep.Submit)
            {
                if (!claudeLimiter.TryAcquire())
                    return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

                var context = await SceneJudgingContext.LoadAsync(db, novel.Id, analysis, cancellationToken);
                var bible = await LoadBible(db, novel.Id, cancellationToken);
                await SubmitBatch(db, novel, BatchKinds.SceneJudgements,
                    () => claude.SubmitSceneJudgements([.. unjudged.Select(context.For)], bible, cancellationToken),
                    cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }

            if (step == BatchStep.Poll && await PollBatch(db, batch!, cancellationToken))
            {
                var chapters = unjudged.ToDictionary(scene => scene.Number, scene => scene.ChapterLabel);
                var results = await claude.SceneJudgementResults(batch!.BatchId, chapters, cancellationToken);
                var byNumber = unjudged.ToDictionary(scene => scene.Number);
                foreach (var result in results)
                {
                    // Matched by the scene's own number, never by position (release A3).
                    if (result.Value is { } judgement && byNumber.TryGetValue(result.SceneNumber, out var judged))
                        judged.JudgementJson = JsonSerializer.Serialize(judgement, JsonResponse.CaseInsensitive);
                }
                batch.CompletedAtUtc = clock.UtcNow;
                logger.LogInformation(
                    "Scene judgement batch {BatchId} for manuscript {NovelId}: {Judged} of {Count} judged; the rest go one at a time",
                    batch.BatchId, novel.Id, results.Count(result => result.Value is not null), unjudged.Count);
                Touch(novel);
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }

            if (step == BatchStep.Abandon)
            {
                await AbandonBatch(db, novel, batch!, cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }

            if (step == BatchStep.OneByOne)
            {
                var scene = unjudged[0];
                if (!claudeLimiter.TryAcquire())
                    return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

                try
                {
                    var context = await SceneJudgingContext.LoadAsync(db, novel.Id, analysis, cancellationToken);
                    var bible = await LoadBible(db, novel.Id, cancellationToken);
                    var judgement = await claude.JudgeScene(context.For(scene), bible, cancellationToken);
                    scene.JudgementJson = JsonSerializer.Serialize(judgement, JsonResponse.CaseInsensitive);
                    ClearCallFailures(novel);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    scene.JudgementAttempts++;
                    await db.SaveChangesAsync(cancellationToken);
                    return await DeferAfterFailedCall(db, novel, ex, $"Judging scene {scene.Number}", cancellationToken);
                }

                Touch(novel);
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }

            // A batch is running (polled and not ended, or polled a moment ago): carry on with the reads.
        }

        // 2g) The sixteen passes. Eight whole-book sections on the standard model, then six readers
        // on the strong one, then the two synthesis passes that read the other fourteen.
        //
        // One pass per turn, each persisted the moment it succeeds, because each is a separate model
        // call costing real money: a reader that comes back short must not re-bill the fourteen that
        // were fine. The ORDER is not cosmetic — sections are grouped before readers so the manuscript
        // is written to the cache once per model rather than once per alternation (F50), and the cheap
        // model goes first so a failing key fails cheaply.
        // The reads on the batch tier (owner's ruling, R39). For each model, one pass runs directly first to
        // write the manuscript to that model's cache (each model keeps its own, F50); the rest of that
        // model's passes then go in one batch. Whatever the batch does not complete runs one at a time below.
        if (await BatchTheReads(db, novel, analysis, cancellationToken) is { } batched)
            return batched;

        if (await NextUnreadPass(db, novel.Id, cancellationToken) is { } due)
        {
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

            var row = await db.PassReads
                .FirstOrDefaultAsync(pass => pass.NovelId == novel.Id && pass.PassId == due.Id, cancellationToken);
            if (row is null)
            {
                row = new PassRead { NovelId = novel.Id, PassId = due.Id };
                db.PassReads.Add(row);
            }

            try
            {
                row.Json = due.IsSynthesis
                    ? JsonSerializer.Serialize(
                        await claude.Synthesise(
                            due.Id,
                            await ReadSections(db, novel.Id, cancellationToken),
                            await ReadReaders(db, novel.Id, cancellationToken),
                            novel.WordCount,
                            Folio.Domain.Markup.FindingCatalogue.Of(await FoldReport(
                                db, novel, analysis, novel.WordCount,
                                await SceneJudgements(db, novel.Id, cancellationToken),
                                SynthesisRead.Empty, cancellationToken)),
                            await PreviousPlan(db, novel, cancellationToken),
                            cancellationToken),
                        JsonResponse.CaseInsensitive)
                    : due.Persona!.Kind == PersonaKind.Reader
                        ? JsonSerializer.Serialize(
                            await claude.ReadAsReader(due.Persona, await Manuscript(db, novel.Id, cancellationToken),
                                await Frame(db, novel, analysis, cancellationToken), cancellationToken),
                            JsonResponse.CaseInsensitive)
                        : JsonSerializer.Serialize(
                            await claude.ReadSection(due.Persona, await Manuscript(db, novel.Id, cancellationToken),
                                await Frame(db, novel, analysis, cancellationToken), cancellationToken),
                            JsonResponse.CaseInsensitive);

                row.CompletedAtUtc = clock.UtcNow;
                row.LastError = null;
                ClearCallFailures(novel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.Attempts++;
                row.LastError = ex.Message;
                await db.SaveChangesAsync(cancellationToken);
                return await DeferAfterFailedCall(db, novel, ex, $"Reading pass '{due.Id}'", cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 2g2) Read this book into its series' bible (release G, R20), once per draft, straight after the
        // reads so the manuscript is still in the cache. A series book only.
        if (novel.SeriesId is { } seriesId && !analysis.SeriesBibleUpdated)
        {
            return await UpdateSeriesBible(db, novel, analysis, seriesId, cancellationToken);
        }

        // 2g3) The round table (release H, R28–R33, R40): the readers talk about the book, one turn a cycle,
        // the table persisted after each. It is the one unattended step that is not batched (R39): every turn
        // depends on the one before.
        if (!RoundTableCodec.Read(analysis.RoundTableJson).Closed)
        {
            return await RoundTableTurn(db, novel, analysis, cancellationToken);
        }

        // 2h) Paint the jacket (a Gemini call), if nothing has painted or set one yet. Its own resumable
        // step, gated on CoverPath/CoverAttempted rather than a column of its own — there is no report field
        // to hold, only a file and a flag. A refusal (ImageContentBlockedException) settles CoverAttempted
        // without a retry, the same as a scene left without an image: a cover is evidence, not the report,
        // and the render must never wait on one. Skipped entirely once the owner has set or repainted a
        // cover by hand — ProjectService's own SetCoverAsync path, not this gate.
        if (novel.CoverPath is null && !novel.CoverAttempted)
        {
            if (!geminiLimiter.TryAcquire())
                return ProcessResult.Waiting(geminiLimiter.TimeUntilNext(), novel.Id);

            var report = JsonSerializer.Deserialize<BookAnalysisResult>(analysis.ConsolidatedJson, JsonResponse.CaseInsensitive)
                         ?? BookAnalysisResult.Empty;

            try
            {
                var jacket = await gemini.GenerateJacket(novel.Title, report.Genre, report.Logline, novel.ArtStyle, cancellationToken);
                novel.CoverPath = imageStore.Save(
                    Path.Combine(novel.Id.ToString(), "covers", $"{Guid.NewGuid():N}.png"), jacket);
                novel.CoverIsOwners = false;
                ClearCallFailures(novel);
            }
            catch (ImageContentBlockedException blocked)
            {
                logger.LogWarning("Manuscript {NovelId} left without a painted jacket after a refusal: {Reason}",
                    novel.Id, blocked.Reason);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, "GenerateJacket", cancellationToken);
            }

            novel.CoverAttempted = true;
            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 3a) The report waits for every scene's own judgement; only a running batch can leave one undone here.
        if (await db.Scenes.AnyAsync(scene => scene.NovelId == novel.Id && scene.JudgementJson == null, cancellationToken))
            return ProcessResult.Waiting(BatchPlan.PollEvery, novel.Id);

        // 3) Render: compose the report PDF from the consolidated report (all parts folded into one).
        if (analysis.PdfStatus != WorkStatus.Completed)
        {
            try
            {
                var wordCount = await WordCount(db, novel.Id, cancellationToken);
                var scenes = await GatherSceneEvidence(db, novel.Id, cancellationToken);
                var review = await BuildDraftReview(db, novel, cancellationToken);
                var synthesis = await ReadSynthesis(db, novel.Id, cancellationToken);
                var folded = await FoldReport(
                    db, novel, analysis, wordCount,
                    [.. scenes.Select(scene => scene.Judgement).OfType<SceneJudgement>()], synthesis, cancellationToken);
                // The draft's own text, which the report marks the findings onto (Release G). Read
                // here rather than carried through the reduce, because it is the largest thing in the
                // database and every earlier step works from notes about it.
                var manuscript = await db.SourceTexts
                    .Where(source => source.NovelId == novel.Id)
                    .Select(source => source.FullText)
                    .FirstOrDefaultAsync(cancellationToken);

                var chapters = await db.Chapters
                    .Where(chapter => chapter.NovelId == novel.Id)
                    .OrderBy(chapter => chapter.Number)
                    .Select(chapter => new Folio.Domain.Markup.ChapterSpan(
                        chapter.Number, chapter.StartOffset, chapter.EndOffset))
                    .ToListAsync(cancellationToken);

                var pdf = analysisPdfComposer.Compose(
                    folded, BuildCover(novel, wordCount), scenes, review, manuscript, chapters);
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
        FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var analysis = await db.BookAnalyses
            .FirstOrDefaultAsync(a => a.NovelId == novel.Id, cancellationToken);
        var reportReady = analysis is { PdfStatus: WorkStatus.Completed, PdfPath: not null };

        // Deliver to the reMarkable tablet FIRST and independently of Drive, so it still runs before the
        // Drive scope is granted (or when Drive is off). One unit of work per cycle, resumable, best-effort.
        // A regenerated report goes to Drive only (owner's ruling): the tablet keeps the copy it has.
        if (reportReady && !novel.Regenerating && archive.DeliverToRemarkable && delivery is { CanDeliverToRemarkable: true } && !analysis!.DeliveredToRemarkable)
        {
            var remarkableResult = await DeliverToRemarkable(db, novel, analysis, cancellationToken);
            if (remarkableResult is { } result)
                return result;
            // null = reMarkable settled (delivered or given up); fall through to the Drive target.
        }

        var reportPending = reportReady && archive.Enabled && delivery is { CanArchiveToDrive: true } && analysis!.ArchivedDriveFileId is null;

        if (!reportPending)
        {
            // The series bible goes beside the report (R26), once the report has gone wherever it was going.
            if (reportReady && novel.SeriesId is not null && analysis!.SeriesBibleUpdated && !analysis.SeriesBibleDelivered)
                return await DeliverSeriesBible(db, novel, analysis, cancellationToken);
            return await FinishArchiving(db, novel, cancellationToken);
        }

        try
        {
            // Resolve (and remember) the destination once. A manuscript whose source has no resolvable
            // parent — a shared file, or one at the root of a shared drive — has nowhere sensible to
            // archive to, which is a reason to skip rather than to fail.
            if (novel.ArchiveFolderId is null)
            {
                var parent = await delivery!.FindDriveParentFolder(novel.GoogleDocId, cancellationToken);
                if (parent is null)
                {
                    logger.LogInformation(
                        "Manuscript {NovelId}: the source document has no resolvable Drive folder, so there is " +
                        "nowhere to archive to; finishing without archiving", novel.Id);
                    return await FinishArchiving(db, novel, cancellationToken);
                }

                // Archive BESIDE the manuscript — into its OWN Drive folder, not a fresh "<Title>"
                // subfolder (owner: the old New-folder behaviour was always wrong). The report's file
                // name already distinguishes it from the source and from other drafts.
                novel.ArchiveFolderId = parent;
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }

            analysis!.ArchivedDriveFileId = await delivery!.UploadDriveFile(
                novel.ArchiveFolderId, DeliveredDocument.FileName(novel.Title, novel.DraftNumber), "application/pdf",
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
            novel.LastError = $"Could not archive to Drive: {ex.Message}";
            if (NetworkFailure.IsOffline(ex))
                return await WaitForNetwork(db, novel, ex, "Archiving to Drive", cancellationToken);

            novel.StageAttempts++;
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

    // Prints the series bible as this draft left it and sends it to the tablet's root and to the Drive folder
    // the report went to (R26). Best-effort, one try at each target: the bible is a convenience beside the
    // report, so a target that fails (offline included) is logged and left, never retried into duplicates,
    // and never holds the review.
    private async Task<ProcessResult> DeliverSeriesBible(
        FolioDbContext db, Novel novel, BookAnalysis analysis, CancellationToken cancellationToken)
    {
        var (readable, bible) = await LoadSeriesBible(db, novel, cancellationToken);
        var placements = (await db.Novels
                .Where(other => other.SeriesId == novel.SeriesId && other.PlacementJson != null && other.DeletedAtUtc == null)
                .Select(other => new { other.ProjectId, other.DraftNumber, other.PlacementJson })
                .ToListAsync(cancellationToken))
            .GroupBy(other => other.ProjectId)
            .Select(book => book.OrderByDescending(draft => draft.DraftNumber).First())
            .Select(book => (book.ProjectId, Placement: StoryPlacementCodec.Read(book.PlacementJson)))
            .Where(book => book.Placement is not null)
            .ToDictionary(book => book.ProjectId, book => book.Placement!);
        var series = string.IsNullOrWhiteSpace(novel.SeriesName)
            ? bible.Books.FirstOrDefault()?.Title ?? novel.Title
            : novel.SeriesName;
        var pdf = readable ? SeriesBibleDocument.RenderOrNull(series, bible, placements) : null;

        if (pdf is not null)
        {
            var name = DeliveredDocument.BibleName(series, novel.Title, novel.DraftNumber);
            if (!novel.Regenerating && archive.DeliverToRemarkable && delivery is { CanDeliverToRemarkable: true } && RemarkableLimits.TooLarge(pdf.Length) is null)
            {
                try
                {
                    await using var stream = new MemoryStream(pdf, writable: false);
                    await delivery.DeliverToRemarkableRoot(name, stream, pdf.Length, cancellationToken);
                    logger.LogInformation("Delivered the series bible of manuscript {NovelId} to the reMarkable root", novel.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Manuscript {NovelId}: the series bible did not reach the reMarkable; leaving it", novel.Id);
                }
            }

        }

        // Drive keeps the bible as one Google Doc per series, rewritten in place, so its version history
        // shows what each book changed (owner's request), rather than a PDF per draft.
        if (readable)
        {
            await new SeriesBibleBackup(dbContextFactory, delivery, archiveOptions, clock, logger)
                .WriteAsync(novel.SeriesId!.Value, $"after {novel.Title}, draft {novel.DraftNumber}", novel.ArchiveFolderId, cancellationToken);
        }

        analysis.SeriesBibleDelivered = true;
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // Delivers the finished report to the ROOT of the owner's reMarkable tablet — the same proven path
    // Foreword's daily delivery uses. (It previously uploaded into a "Canonizer" collection via
    // EnsureFolder + a folder-parented upload, but that folder/collection wire format was reconstructed and
    // never verified against the live cloud; it silently delivered nothing. Root delivery is the fix.)
    // Best-effort and independent of Drive: a failure never blocks the review from finishing. Returns a
    // ProcessResult to return (did work / waiting on a retry) while delivery is in flight, or null once it
    // has SETTLED — delivered, or given up after the shared attempt budget — so the caller continues to the
    // Drive target. Resumable: DeliveredToRemarkable is set only on a full success (or a give-up), so a
    // worker killed mid-upload simply re-attempts next cycle.
    private async Task<ProcessResult?> DeliverToRemarkable(
        FolioDbContext db, Novel novel, BookAnalysis analysis, CancellationToken cancellationToken)
    {
        // A file over the tablet's limit is refused before it is even opened: on a real book a 173 MB report ran
        // the phone out of memory, six times over. The limit is the shared one (RemarkableLimits).
        var size = imageStore.Size(analysis.PdfPath!);
        if (RemarkableLimits.TooLarge(size) is { } tooLarge)
        {
            logger.LogWarning(
                "Manuscript {NovelId}: the report is {SizeMb:0} MB, over the {LimitMb:0} MB the reMarkable takes; " +
                "not sending it to the tablet (the report remains available elsewhere)",
                novel.Id, size / 1_000_000.0, RemarkableLimits.MaxBytes / 1_000_000.0);
            return await SettleWithoutTablet(db, novel, analysis,
                $"It did not reach the reMarkable: {tooLarge.Sentence}.", cancellationToken);
        }

        try
        {
            // Streamed from the file rather than read into memory first (efficiency audit §18).
            await using (var report = imageStore.OpenRead(analysis.PdfPath!))
            {
                await delivery!.DeliverToRemarkableRoot(
                    DeliveredDocument.Name(novel.Title, novel.DraftNumber), report, size, cancellationToken);
            }

            analysis.DeliveredToRemarkable = true;
            analysis.RemarkableNote = null;
            novel.StageAttempts = 0;
            novel.LastError = null;
            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Delivered the revision report of manuscript {NovelId} to the reMarkable root", novel.Id);
            return ProcessResult.DidWork(novel.Id);
        }
        catch (RemarkableRefusedException refused)
        {
            // Final, not a failure to retry: the same file in the same heap runs out again, and each attempt
            // fills the heap further before it does.
            logger.LogWarning(refused,
                "Manuscript {NovelId}: the reMarkable delivery was refused ({Reason}); not repeating it with the " +
                "same file ({SizeMb:0} MB)", novel.Id, refused.Reason, size / 1_000_000.0);
            return await SettleWithoutTablet(db, novel, analysis,
                $"It did not reach the reMarkable: {refused.Sentence}.", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            novel.LastError = $"Could not deliver to reMarkable: {ex.Message}";
            if (NetworkFailure.IsOffline(ex))
                return await WaitForNetwork(db, novel, ex, "Delivering to the reMarkable", cancellationToken);

            novel.StageAttempts++;
            if (novel.StageAttempts >= rateLimits.MaxAttempts)
            {
                // Give up so the review can still finish (the report is on the device and, if enabled, Drive).
                // Nothing reached the tablet — logged distinctly from a success so diagnostics show the gap.
                logger.LogWarning(ex,
                    "Manuscript {NovelId}: giving up on reMarkable delivery after {Attempts} attempts; nothing " +
                    "was delivered to the tablet, continuing (the report remains available elsewhere)",
                    novel.Id, novel.StageAttempts);
                var attempts = novel.StageAttempts;
                var cause = Storyworthtelling.Diagnostics.PlainError.Describe(ex.Message, "reMarkable");
                return await SettleWithoutTablet(db, novel, analysis,
                    $"It did not reach the reMarkable after {attempts} tries" +
                    (cause.Length == 0 ? "." : $": {cause}."), cancellationToken);
            }

            var delay = backoff.NextDelay(novel.StageAttempts - 1, (ex as RateLimitException)?.RetryAfter);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.Waiting(delay, novel.Id);
        }
    }

    // The tablet step is over without the report having reached it. Settled, so the review goes on to the
    // Drive target and finishes — but the reason is kept on the report, because the error recorded on the
    // manuscript is cleared by the very next successful step and the owner would never otherwise know.
    private async Task<ProcessResult?> SettleWithoutTablet(
        FolioDbContext db, Novel novel, BookAnalysis analysis, string note, CancellationToken cancellationToken)
    {
        analysis.DeliveredToRemarkable = true;
        analysis.RemarkableNote = note;
        novel.StageAttempts = 0;      // hand a fresh budget to the Drive target
        novel.LastError = null;
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // Leaves the archive stage: stamps when it happened (only when the report was actually uploaded),
    // optionally reclaims the local copy, and advances.
    private async Task<ProcessResult> FinishArchiving(
        FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var uploaded = novel.ArchiveFolderId is not null && await db.BookAnalyses
            .AnyAsync(a => a.NovelId == novel.Id && a.ArchivedDriveFileId != null, cancellationToken);
        if (uploaded)
        {
            novel.ArchivedAtUtc = clock.UtcNow;
            if (archive.FreeLocalCopies)
            {
                // Not this draft's own scenes: the next draft pairs its changed scenes against them
                // (F41) and this is the only copy. SceneRetention says which are actually finished.
                var siblings = await db.Novels
                    .Where(n => n.ProjectId == novel.ProjectId)
                    .Select(n => n.Id)
                    .ToListAsync(cancellationToken);
                foreach (var finished in SceneRetention.Freeable(novel.Id, novel.PreviousDraftId, siblings))
                {
                    imageStore.Delete($"{finished}/scenes");
                }

                logger.LogInformation(
                    "Freed the scene images of the finished drafts of project {ProjectId} after archiving", novel.ProjectId);
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
        FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, SourceIngested: false, AllChunksAnalyzed: false, BibleConsolidated: false,
            AllReferenceSheetsRendered: false, AllSceneImagesRendered: false, AnalysisComplete: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // --- helpers ---

    // A delivery step (the Drive archive, the tablet) that could not reach the network. Nothing left the
    // phone, so it is not a try at the work: no attempt is spent, and the wait is stored on the manuscript
    // (RetryNotBeforeUtc, honoured by Dispatch) so the next worker cycle does not ask again at once.
    private async Task<ProcessResult> WaitForNetwork(
        FolioDbContext db, Novel novel, Exception exception, string step, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(rateLimits.OfflineRetrySeconds);
        novel.RetryNotBeforeUtc = clock.UtcNow + delay;
        logger.LogWarning(exception,
            "{Step} could not reach the network for manuscript {NovelId}: the phone is offline; this does not " +
            "count as an attempt; next check after {NextAttempt:u}: {Error}",
            step, novel.Id, novel.RetryNotBeforeUtc, ErrorText.Describe(exception));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.Waiting(delay, novel.Id);
    }

    // Records a failed once-per-manuscript call (a reduction, one of the sixteen passes, the jacket) and
    // decides what happens next. These calls each send the whole book, so a failure that is simply
    // retried is a failure that is simply re-billed:
    //  - it is LOGGED, by name, with the attempt it was and the cause all the way down the inner chain;
    //  - it is PACED: each failure waits longer than the last, and the wait is stored on the manuscript
    //    (RetryNotBeforeUtc) so that the next worker cycle — a new process — honours it too;
    //  - it is CAPPED: after MaxAttempts the manuscript is parked, with its error, until the owner taps
    //    Retry (CallGate). StageAttempts documents this bound; nothing applied it until now.
    // A provider rate limit is different in kind: it was refused before any work was done, so it never
    // spends the budget — it waits as long as the provider asked, or, for an empty credit balance, for
    // the long flat interval the per-unit steps use (no point asking again until someone tops up).
    private async Task<ProcessResult> DeferAfterFailedCall(
        FolioDbContext db, Novel novel, Exception exception, string call, CancellationToken cancellationToken)
    {
        var cause = ErrorText.Describe(exception);
        novel.LastError = $"{call}: {cause}";

        TimeSpan delay;
        if (NetworkFailure.IsOffline(exception))
        {
            // Nothing reached the provider, so nothing was billed and nothing was learned about the call:
            // wait a short flat interval and ask again without spending an attempt.
            delay = TimeSpan.FromSeconds(rateLimits.OfflineRetrySeconds);
            novel.RetryNotBeforeUtc = clock.UtcNow + delay;
            logger.LogWarning(exception,
                "{Call} could not reach the network for manuscript {NovelId}: the phone is offline; this does " +
                "not count as an attempt; next check after {NextAttempt:u}: {Error}",
                call, novel.Id, novel.RetryNotBeforeUtc, cause);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.Waiting(delay, novel.Id);
        }

        if (exception is RateLimitException rateLimit)
        {
            delay = rateLimit.Kind == RateLimitKind.QuotaExhausted
                ? TimeSpan.FromSeconds(rateLimits.MaxDelaySeconds)
                : backoff.NextDelay(0, rateLimit.RetryAfter);
            novel.RateLimitedUntilUtc = clock.UtcNow + delay;
            novel.RetryNotBeforeUtc = clock.UtcNow + delay;
            logger.LogWarning(exception,
                "{Call} deferred for manuscript {NovelId}: provider {Kind}; next try after {NextAttempt:u}" +
                (rateLimit.Kind == RateLimitKind.QuotaExhausted ? " (add billing to resume)" : "") + ": {Error}",
                call, novel.Id, rateLimit.Kind == RateLimitKind.QuotaExhausted ? "quota/credits exhausted" : "rate limit",
                novel.RetryNotBeforeUtc, cause);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.Waiting(delay, novel.Id);
        }

        novel.StageAttempts++;
        var truncated = exception is ResponseTruncatedException;
        if (novel.StageAttempts >= rateLimits.MaxAttempts ||
            (truncated && novel.StageAttempts >= rateLimits.MaxTruncatedAttempts))
        {
            var attemptsMade = novel.StageAttempts;
            // Parked is "attempts spent and an error recorded" (CallGate, and the card's needs-attention
            // line, both read it that way), so a truncation that stops early spends the rest of the budget.
            novel.StageAttempts = rateLimits.MaxAttempts;
            novel.RetryNotBeforeUtc = null;
            var reason = truncated
                ? $" (the reply was truncated at the output limit, and a truncated reply is truncated again: " +
                  $"such a call is tried at most {rateLimits.MaxTruncatedAttempts} times)"
                : string.Empty;
            logger.LogWarning(exception,
                "{Call} failed for manuscript {NovelId} on attempt {Attempt}/{Max}; giving up and parking it " +
                "until Retry is tapped, so the book is not sent again unattended{Reason}: {Error}",
                call, novel.Id, attemptsMade, rateLimits.MaxAttempts, reason, cause);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.Idle;
        }

        delay = backoff.NextDelay(novel.StageAttempts - 1);
        novel.RetryNotBeforeUtc = clock.UtcNow + delay;
        logger.LogWarning(exception,
            "{Call} failed for manuscript {NovelId} (attempt {Attempt}/{Max}); next try after {NextAttempt:u}: {Error}",
            call, novel.Id, novel.StageAttempts, rateLimits.MaxAttempts, novel.RetryNotBeforeUtc, cause);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.Waiting(delay, novel.Id);
    }

    // A call that worked: the failures before it no longer count, and nothing is waiting any more.
    private static void ClearCallFailures(Novel novel)
    {
        novel.LastError = null;
        novel.StageAttempts = 0;
        novel.RetryNotBeforeUtc = null;
    }

    // Idle when a once-per-manuscript call has spent its attempts (parked until the owner retries),
    // Waiting while a backoff set by an earlier failure is still running, otherwise null: go ahead.
    private ProcessResult? CallGate(Novel novel, DateTimeOffset now)
    {
        if (novel.LastError is not null && novel.StageAttempts >= rateLimits.MaxAttempts)
            return ProcessResult.Idle;
        if (novel.RetryNotBeforeUtc is { } notBefore && notBefore > now)
            return ProcessResult.Waiting(notBefore - now, novel.Id);
        return null;
    }

    // Detects the manuscript's chapters exactly once (guarded by Novel.ChaptersDetected) and stamps
    // every not-yet-labelled chunk with the chapters its [StartOffset, EndOffset) overlaps, so a
    // finding drawn from that chunk can cite where in the book it came from. Idempotent and cheap once
    // settled. Returns whether anything changed (the caller treats that as one unit of work).
    private async Task<bool> EnsureChaptersLabelled(
        FolioDbContext db, Novel novel, string? fullText, CancellationToken cancellationToken)
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
        if (NetworkFailure.IsOffline(exception))
        {
            // The phone has no network: the request never left it. That is not an attempt at the work, so it
            // is not counted, and the whole manuscript waits rather than every unit failing in turn.
            unit.LastError = ErrorText.Describe(exception);
            unit.WorkState = WorkStatus.NeedsRetry;
            unit.NextAttemptAtUtc = clock.UtcNow + TimeSpan.FromSeconds(rateLimits.OfflineRetrySeconds);
            novel.RetryNotBeforeUtc = unit.NextAttemptAtUtc;
            logger.LogWarning(exception,
                "{Unit} could not reach the network: the phone is offline; this does not count as an attempt " +
                "({Attempts}/{Max} used); next check {NextAttempt:u}: {Error}",
                unit.GetType().Name, unit.Attempts, rateLimits.MaxAttempts, unit.NextAttemptAtUtc,
                ErrorText.Describe(exception));
            return;
        }

        unit.Attempts++;
        unit.LastError = exception.Message;
        var rateLimit = exception as RateLimitException;
        var transient = rateLimit is not null;

        if (!transient && unit.Attempts >= rateLimits.MaxAttempts)
        {
            unit.WorkState = WorkStatus.Failed;
            logger.LogWarning(exception, "{Unit} failed after {Attempts} attempts and is given up on: {Error}",
                unit.GetType().Name, unit.Attempts, ErrorText.Describe(exception));
            return;
        }

        unit.WorkState = WorkStatus.NeedsRetry;

        // Depleted billing credits won't come back on the normal backoff — retrying in seconds just burns
        // calls until the user tops up. Defer a long, flat interval and don't advertise a resume time we
        // can't predict; the error message (which names billing) is what tells the user how to unblock it.
        if (rateLimit?.Kind == RateLimitKind.QuotaExhausted)
        {
            unit.NextAttemptAtUtc = clock.UtcNow + TimeSpan.FromSeconds(rateLimits.MaxDelaySeconds);
            logger.LogWarning(exception, "{Unit} deferred: provider quota/credits exhausted; next check {NextAttempt:u} (add billing to resume): {Error}",
                unit.GetType().Name, unit.NextAttemptAtUtc, ErrorText.Describe(exception));
            return;
        }

        unit.NextAttemptAtUtc = clock.UtcNow + backoff.NextDelay(unit.Attempts - 1, rateLimit?.RetryAfter);
        // A provider rate limit is the "when do queries resume" the user wants to see, so record the
        // expected resume time on the manuscript; a plain error retry doesn't (it's not a quota wait).
        if (transient)
            novel.RateLimitedUntilUtc = unit.NextAttemptAtUtc;
        logger.LogWarning(exception, "{Unit} attempt {Attempts}/{Max} failed ({Kind}); retrying after {NextAttempt:u}: {Error}",
            unit.GetType().Name, unit.Attempts, rateLimits.MaxAttempts,
            transient ? "rate limit" : "error", unit.NextAttemptAtUtc, ErrorText.Describe(exception));
    }

    // The manuscript's word count, so the report can frame length against the genre norm.
    private static async Task<int> WordCount(FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var text = await db.SourceTexts
            .Where(s => s.NovelId == novelId)
            .Select(s => s.FullText)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    // Flattens the consolidated report's four story-integrity finding lists into reconciliation candidates,
    // stamping each with its now-first-class category and a stable index (its position in this combined list,
    // which is how the model refers back to it).
    private static IReadOnlyList<CandidateFinding> ToCandidateFindings(BookAnalysisResult report)
    {
        var candidates = new List<CandidateFinding>();
        void Add(FindingCategory category, IReadOnlyList<ReportFinding> findings)
        {
            foreach (var finding in findings)
                candidates.Add(new CandidateFinding(candidates.Count, category, finding.Chapter, finding.Detail));
        }

        Add(FindingCategory.PlotHole, report.PlotHoles);
        Add(FindingCategory.Inconsistency, report.Inconsistencies);
        Add(FindingCategory.Inaccuracy, report.Inaccuracies);
        Add(FindingCategory.Pacing, report.PacingNotes);
        return candidates;
    }

    // Builds the prior finding threads the reconciliation matches against: every thread ever seen in the
    // project (so a fixed issue that reappears reads as a regression), represented by its most recent
    // occurrence's text, and flagged OpenInPreviousDraft when it had an occurrence in the draft this one was
    // made from (an unmatched open thread is what "resolved this draft" means).
    private async Task<IReadOnlyList<PriorFinding>> LoadPriorThreads(
        FolioDbContext db, Guid projectId, Guid previousDraftId, CancellationToken cancellationToken)
    {
        var occurrences = await db.Findings
            .Where(f => f.ProjectId == projectId)
            .Select(f => new
            {
                f.ThreadId, f.NovelId, f.Category, f.Chapter, f.Detail, f.FirstDraftNumber, f.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return occurrences
            .GroupBy(f => f.ThreadId)
            .Select(thread =>
            {
                var latest = thread.OrderBy(f => f.CreatedAtUtc).Last();
                return new PriorFinding(
                    thread.Key, latest.Category, latest.Chapter, latest.Detail,
                    FirstDraftNumber: thread.Min(f => f.FirstDraftNumber),
                    OpenInPreviousDraft: thread.Any(f => f.NovelId == previousDraftId));
            })
            .ToList();
    }

    // One turn of the round table: the host's move, a reader's turn on its cue, or the close. The table is
    // saved after every turn; a failed call is retried on its own, so the show resumes rather than restarts.
    private async Task<ProcessResult> RoundTableTurn(
        FolioDbContext db, Novel novel, BookAnalysis analysis, CancellationToken cancellationToken)
    {
        var reviews = await ReadReaders(db, novel.Id, cancellationToken);
        var length = ManuscriptLength.Classify(novel.WordCount);
        var table = RoundTableCodec.Read(analysis.RoundTableJson);
        // The agenda and cap are fixed as the show begins (owner's ruling: disagreements first, then structure,
        // themes, characters and whatever else the reads raised).
        if (table.Turns.Count == 0 && table.Agenda.Count == 0)
            table = RoundTablePlan.Begin(RoundTableAgenda.For(await ReadSections(db, novel.Id, cancellationToken)), RoundTablePlan.TurnCap(length));
        var step = RoundTablePlan.Next(table, reviews, length);
        if (reviews.Count == 0 || step.Kind == RoundTableStepKind.Done)
        {
            // Nobody to talk: the table closes empty rather than holding the report.
            analysis.RoundTableJson = RoundTableCodec.Write(table with { Summary = table.Summary ?? new RoundTableSummary([], []) });
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (!claudeLimiter.TryAcquire())
            return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

        try
        {
            switch (step.Kind)
            {
                case RoundTableStepKind.HostTurn:
                    var move = await claude.RoundTableHost(
                        reviews, table, RoundTablePlan.Owed(table, reviews),
                        Math.Max(0, table.Cap - table.Turns.Count), length, cancellationToken);
                    table = RoundTablePlan.Apply(table, move, reviews);
                    break;
                case RoundTableStepKind.ReaderTurn:
                    var cued = table with { Next = step.Cue };
                    var words = await claude.RoundTableSpeak(
                        PersonaCatalogue.ById(step.Cue!.Speaker), reviews, cued, step.Cue, cancellationToken);
                    table = RoundTablePlan.Spoken(cued, words);
                    break;
                default:
                    var closing = await claude.RoundTableClose(reviews, table, cancellationToken);
                    table = RoundTablePlan.Closed(table, closing.Says, closing.Summary);
                    break;
            }
            ClearCallFailures(novel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await DeferAfterFailedCall(db, novel, ex, $"Round table turn {table.Turns.Count + 1}", cancellationToken);
        }

        analysis.RoundTableJson = RoundTableCodec.Write(table);
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // Merges this draft's read into its series' bible. Earlier books not yet in the bible are seeded from
    // their stored canon first, at no cost, so a series reviewed before the bible existed is not read again.
    // A stored bible that will not read is never written over: this draft settles without it, and the
    // review goes on.
    private async Task<ProcessResult> UpdateSeriesBible(
        FolioDbContext db, Novel novel, BookAnalysis analysis, Guid seriesId, CancellationToken cancellationToken)
    {
        var record = await db.SeriesBibles.FirstOrDefaultAsync(bible => bible.SeriesId == seriesId, cancellationToken);
        var (readable, bible) = await LoadSeriesBible(db, novel, cancellationToken);
        if (!readable)
        {
            logger.LogWarning(
                "The series bible of manuscript {NovelId} could not be read; leaving it as it is and finishing " +
                "the review without reading this book into it", novel.Id);
            analysis.SeriesBibleUpdated = true;
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (!claudeLimiter.TryAcquire())
            return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

        var manuscript = await Manuscript(db, novel.Id, cancellationToken);
        IReadOnlyList<BibleFact> facts;
        try
        {
            facts = await claude.ReadSeriesBible(
                manuscript, await Frame(db, novel, analysis, cancellationToken), novel.Title,
                BibleText.For(bible.Without(novel.ProjectId)), cancellationToken);
            ClearCallFailures(novel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await DeferAfterFailedCall(db, novel, ex, "ReadSeriesBible", cancellationToken);
        }

        // Read again after the call, which can take a minute: a correction the owner made meanwhile is kept.
        record ??= await db.SeriesBibles.FirstOrDefaultAsync(stored => stored.SeriesId == seriesId, cancellationToken);
        (readable, bible) = await LoadSeriesBible(db, novel, cancellationToken);
        if (!readable)
        {
            analysis.SeriesBibleUpdated = true;
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }
        bible = bible.WithBook(
            new SeriesBook(novel.ProjectId, novel.Title, novel.SeriesSequence), BibleQuotes.Keep(manuscript, facts));

        if (record is null)
        {
            record = new SeriesBibleRecord { SeriesId = seriesId };
            db.SeriesBibles.Add(record);
        }
        record.Json = SeriesBibleCodec.Write(bible);
        record.UpdatedAtUtc = clock.UtcNow;
        analysis.SeriesBibleUpdated = true;
        logger.LogInformation(
            "Read manuscript {NovelId} into its series bible: {Entries} entries across {Books} books",
            novel.Id, bible.Entries.Count, bible.Books.Count);
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // The series' bible as stored, with every finished earlier book it does not yet hold seeded from that
    // book's stored canon, at no cost. Readable is false when the stored bible will not read: nothing may
    // then be written over it, and checks fall back to an empty bible.
    private async Task<(bool Readable, SeriesBible Bible)> LoadSeriesBible(
        FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        if (novel.SeriesId is not { } seriesId)
            return (true, SeriesBible.Empty);

        var json = await db.SeriesBibles.Where(record => record.SeriesId == seriesId)
            .Select(record => record.Json).FirstOrDefaultAsync(cancellationToken);
        if (!SeriesBibleCodec.TryRead(json, out var bible))
            return (false, SeriesBible.Empty);

        foreach (var book in (await EarlierBooks(db, novel, cancellationToken)).Where(book => !bible.Has(book.ProjectId)))
        {
            var canon = await LoadSeriesCanon(db, book.DraftId, cancellationToken);
            bible = bible.WithBook(new SeriesBook(book.ProjectId, book.Title, book.Written), BibleSeed.From(canon));
        }
        return (true, bible);
    }

    private sealed record OtherBook(Guid DraftId, Guid ProjectId, string Title, int Written);

    // The series' other finished books, each as its newest finished draft, in the order they were written.
    // A series' id is its first book's project id, which is how a first book read before it was linked is found.
    private static async Task<IReadOnlyList<OtherBook>> EarlierBooks(
        FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var seriesId = novel.SeriesId;
        var drafts = await db.Novels
            .Where(other => (other.SeriesId == seriesId || other.ProjectId == seriesId || other.Id == novel.PreviousBookId)
                            && other.ProjectId != novel.ProjectId
                            && other.Stage == ProjectStage.Completed
                            && other.DeletedAtUtc == null)
            .Select(other => new { other.Id, other.ProjectId, other.Title, other.SeriesSequence, other.DraftNumber })
            .ToListAsync(cancellationToken);
        return [.. drafts.GroupBy(other => other.ProjectId)
            .Select(book => book.OrderByDescending(draft => draft.DraftNumber).First())
            .OrderBy(book => book.SeriesSequence)
            .Select(book => new OtherBook(book.Id, book.ProjectId, book.Title, book.SeriesSequence))];
    }

    // Assembles the prior book's canon for the cross-book continuity read (Phase 5): its title, its design-bible
    // cast/settings (name + established description), where its story stood (its continuity summary), and its
    // still-open finding threads. priorBookId is the specific prior-book draft the sequel was linked to (the
    // prior book's latest-completed draft, resolved at link time).
    private async Task<SeriesCanon> LoadSeriesCanon(
        FolioDbContext db, Guid priorBookId, CancellationToken cancellationToken)
    {
        var prior = await db.Novels
            .Where(n => n.Id == priorBookId)
            .Select(n => new { n.Title, n.ProjectId, n.ContinuitySummary })
            .FirstOrDefaultAsync(cancellationToken);
        if (prior is null)
            return SeriesCanon.Empty;

        var refs = await db.DesignRefs
            .Where(r => r.NovelId == priorBookId)
            .Select(r => new { r.Kind, r.Name, r.CanonicalDescription })
            .ToListAsync(cancellationToken);
        var cast = refs.Where(r => r.Kind == ReferenceKind.Character)
            .Select(r => new CanonEntity(r.Name, r.CanonicalDescription)).ToList();
        var settings = refs.Where(r => r.Kind == ReferenceKind.Setting)
            .Select(r => new CanonEntity(r.Name, r.CanonicalDescription)).ToList();

        var openThreads = await LoadOpenThreads(db, prior.ProjectId, priorBookId, cancellationToken);
        return new SeriesCanon(prior.Title, cast, settings, prior.ContinuitySummary, openThreads);
    }

    // The prior book's still-open finding threads — a thread with an occurrence in the canon draft is unresolved
    // (the inverse of the resolved-set logic in BuildDraftReview). Each is returned as its latest occurrence's
    // text, so the continuity read can judge whether this book addresses, ignores, or contradicts it.
    private static async Task<IReadOnlyList<ReportFinding>> LoadOpenThreads(
        FolioDbContext db, Guid priorProjectId, Guid canonDraftId, CancellationToken cancellationToken)
    {
        var occurrences = await db.Findings
            .Where(f => f.ProjectId == priorProjectId)
            .Select(f => new { f.ThreadId, f.NovelId, f.Chapter, f.Detail, f.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        return occurrences
            .GroupBy(f => f.ThreadId)
            .Where(thread => thread.Any(f => f.NovelId == canonDraftId))   // open = present in the canon draft
            .Select(thread =>
            {
                var latest = thread.OrderBy(f => f.CreatedAtUtc).Last();
                return new ReportFinding(latest.Chapter, latest.Detail);
            })
            .ToList();
    }

    // Builds the cross-draft review the report renders with: this draft's persisted findings (with the
    // status the reconciliation gave them) and the findings resolved since the previous draft (its threads
    // that no current finding claims). A first draft has no history, so it renders without status marks.
    private static async Task<DraftReview> BuildDraftReview(
        FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var findings = (await db.Findings
                .Where(f => f.NovelId == novel.Id)
                .Select(f => new { f.Category, f.Chapter, f.Detail, f.Status })
                .ToListAsync(cancellationToken))
            .Select(f => new ReportFindingView(f.Category, f.Chapter, f.Detail, f.Status))
            .ToList();

        if (novel.PreviousDraftId is not { } previousId)
            return new DraftReview(novel.DraftNumber, findings, Array.Empty<ReportFindingView>());

        var currentThreadIds = (await db.Findings
                .Where(f => f.NovelId == novel.Id)
                .Select(f => f.ThreadId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var resolved = (await db.Findings
                .Where(f => f.NovelId == previousId)
                .Select(f => new { f.ThreadId, f.Category, f.Chapter, f.Detail })
                .ToListAsync(cancellationToken))
            .Where(f => !currentThreadIds.Contains(f.ThreadId))
            .Select(f => new ReportFindingView(f.Category, f.Chapter, f.Detail, FindingStatus.Carried))
            .ToList();

        // Each reader's score on the draft this was made from, so its page can say how it moved.
        var previousScores = (await ReadReaders(db, previousId, cancellationToken))
            .Where(read => read.Score is >= 1 and <= 10)
            .GroupBy(read => read.PersonaId)
            .ToDictionary(group => group.Key, group => group.Last().Score);

        return new DraftReview(novel.DraftNumber, findings, resolved) { PreviousReaderScores = previousScores };
    }

    /// <summary>
    /// The consolidated report with every part folded in: the craft, voice, expansion and continuity
    /// reductions, each scene's own judgement, the fourteen reads and, when given, the two synthesis passes.
    /// The render draws it; the plan's synthesis is shown the findings it holds (R15), so both read one fold.
    /// </summary>
    private async Task<BookAnalysisResult> FoldReport(
        FolioDbContext db, Novel novel, BookAnalysis analysis, int wordCount, IReadOnlyList<SceneJudgement> judgements,
        SynthesisRead synthesis, CancellationToken cancellationToken)
    {
        var report = JsonSerializer.Deserialize<BookAnalysisResult>(analysis.ConsolidatedJson, JsonResponse.CaseInsensitive)
                     ?? BookAnalysisResult.Empty;
        var craft = analysis.CraftJson is null
            ? CraftReport.Empty
            : JsonSerializer.Deserialize<CraftReport>(analysis.CraftJson, JsonResponse.CaseInsensitive) ?? CraftReport.Empty;
        var voice = analysis.VoiceJson is null
            ? VoiceConsistency.Empty
            : JsonSerializer.Deserialize<VoiceConsistency>(analysis.VoiceJson, JsonResponse.CaseInsensitive) ?? VoiceConsistency.Empty;
        var expansion = analysis.ExpansionJson is null
            ? ExpansionReport.Empty
            : JsonSerializer.Deserialize<ExpansionReport>(analysis.ExpansionJson, JsonResponse.CaseInsensitive) ?? ExpansionReport.Empty;
        var seriesContinuity = analysis.SeriesContinuityJson is null
            ? SeriesContinuity.Empty
            : JsonSerializer.Deserialize<SeriesContinuity>(analysis.SeriesContinuityJson, JsonResponse.CaseInsensitive) ?? SeriesContinuity.Empty;

        // The sixteen passes, folded last so the readers replace the omnibus reduce's reviews
        // rather than sitting beside them.
        var sections = await ReadSections(db, novel.Id, cancellationToken);
        var readers = await ReadReaders(db, novel.Id, cancellationToken);

        // Each scene's own judgement (release A3). When the scenes have been judged one by one,
        // the craft reduce's separate scene list is not printed at all: it is the list that used
        // to be zipped against the real scenes by position.
        if (judgements.Count > 0)
            craft = craft with { SceneBreakdown = [] };

        var folded = report.WithCraft(craft).WithVoiceConsistency(voice).WithWordCount(wordCount)
            .WithSceneJudgements(judgements)
            .WithExpansion(expansion).WithSeriesContinuity(seriesContinuity)
            .WithReads(
                readers,
                sections.Where(section => section.HasContent).ToList(),
                synthesis.RevisionPlan,
                synthesis.Verdict);

        // What the last draft's plan asked for that no read raises now (R19).
        var table = RoundTableCodec.Read(analysis.RoundTableJson);
        if (table.Closed)
            folded = folded with { RoundTable = table };
        return synthesis.Fixed is { Count: > 0 } ? folded with { FixedSincePrevious = synthesis.Fixed } : folded;
    }

    /// <summary>Each judged scene's judgement, in reading order, without loading its picture.</summary>
    private static async Task<IReadOnlyList<SceneJudgement>> SceneJudgements(
        FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var stored = await db.Scenes
            .Where(scene => scene.NovelId == novelId && scene.JudgementJson != null)
            .OrderBy(scene => scene.Number)
            .Select(scene => scene.JudgementJson!)
            .ToListAsync(cancellationToken);
        return [.. stored
            .Select(json => JsonSerializer.Deserialize<SceneJudgement>(json, JsonResponse.CaseInsensitive))
            .OfType<SceneJudgement>()];
    }

    // Builds each scene's evidence for the report: the rendered image (if any), cited "Ch. N · Scene M",
    // paired by reading order with the craft read's scene-by-scene note for its purpose and verdict.
    private async Task<IReadOnlyList<SceneEvidence>> GatherSceneEvidence(
        FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var scenes = await db.Scenes
            .Where(s => s.NovelId == novelId)
            .OrderBy(s => s.Number)
            .Select(s => new { s.Number, s.ChapterNumber, s.ChapterLabel, s.ImagePath, s.Changed, s.PreviousSceneId, s.EndOffset, s.JudgementJson })
            .ToListAsync(cancellationToken);

        // The before half of a changed scene's pair (F41). Only for changed scenes: an unchanged one
        // reuses the very image it would be compared against, and the same picture twice is not a
        // before and after.
        var priorIds = scenes
            .Where(s => s.Changed && s.PreviousSceneId is not null)
            .Select(s => s.PreviousSceneId!.Value)
            .ToList();
        var priorPaths = priorIds.Count == 0
            ? []
            : await db.Scenes
                .Where(s => priorIds.Contains(s.Id))
                .Select(s => new { s.Id, s.ImagePath, s.JudgementJson })
                .ToDictionaryAsync(s => s.Id, s => (s.ImagePath, s.JudgementJson), cancellationToken);

        var evidence = new List<SceneEvidence>(scenes.Count);
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var citation = string.IsNullOrWhiteSpace(scene.ChapterLabel)
                ? $"Scene {scene.Number}"
                : $"{scene.ChapterLabel} · Scene {scene.Number}";
            // The scene's OWN judgement, read off its own row (release A3). Never a list zipped by position.
            var judgement = scene.JudgementJson is null
                ? null
                : JsonSerializer.Deserialize<SceneJudgement>(scene.JudgementJson, JsonResponse.CaseInsensitive);
            byte[]? Read(string? path) =>
                path is not null && imageStore.Exists(path) ? imageStore.Read(path) : null;

            byte[]? previous = null;
            SceneJudgement? previousJudgement = null;
            if (scene.Changed && scene.PreviousSceneId is { } priorId
                && priorPaths.TryGetValue(priorId, out var prior))
            {
                previous = Read(prior.ImagePath);
                // The last draft's verdict on the scene this one was matched to (R19).
                previousJudgement = prior.JudgementJson is null
                    ? null
                    : JsonSerializer.Deserialize<SceneJudgement>(prior.JudgementJson, JsonResponse.CaseInsensitive);
            }

            evidence.Add(new SceneEvidence(
                citation, judgement?.Accomplishes ?? string.Empty, judgement?.Verdict ?? string.Empty,
                Read(scene.ImagePath), previous)
            {
                // Where the scene ends, so its block is inserted under its own last line.
                EndOffset = scene.EndOffset,
                Judgement = judgement,
                PreviousJudgement = previousJudgement,
            });
        }
        return evidence;
    }

    /// <summary>
    /// What scenes are judged from, loaded once for a draft: the source text, the story so far at the end
    /// of each notes block, and the book's dominant voice if the voice read has run.
    /// </summary>
    private sealed class SceneJudgingContext
    {
        private readonly string source;
        private readonly IReadOnlyList<TextBlockState> blocks;
        private readonly string voice;

        private SceneJudgingContext(string source, IReadOnlyList<TextBlockState> blocks, string voice)
        {
            this.source = source;
            this.blocks = blocks;
            this.voice = voice;
        }

        public static async Task<SceneJudgingContext> LoadAsync(
            FolioDbContext db, Guid novelId, BookAnalysis analysis, CancellationToken cancellationToken)
        {
            var source = await db.SourceTexts.Where(s => s.NovelId == novelId)
                .Select(s => s.FullText).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            var chunks = await db.Chunks.Where(c => c.NovelId == novelId)
                .Select(c => new { c.Ordinal, c.StartOffset, c.EndOffset }).ToListAsync(cancellationToken);
            var notes = await db.ChunkAnalyses.Where(a => a.NovelId == novelId && a.NotesJson != null)
                .Select(a => new { a.Ordinal, a.NotesJson }).ToListAsync(cancellationToken);
            var blocks = chunks.Join(notes, c => c.Ordinal, n => n.Ordinal, (c, n) => new TextBlockState(
                c.StartOffset, c.EndOffset,
                JsonSerializer.Deserialize<ChapterNotes>(n.NotesJson!, JsonResponse.CaseInsensitive)?.RunningState ?? string.Empty))
                .ToList();
            var voice = analysis.VoiceJson is null
                ? string.Empty
                : JsonSerializer.Deserialize<VoiceConsistency>(analysis.VoiceJson, JsonResponse.CaseInsensitive)?.DominantVoice ?? string.Empty;
            return new SceneJudgingContext(source, blocks, voice);
        }

        public SceneToJudge For(Scene scene)
        {
            var start = Math.Clamp(scene.StartOffset, 0, source.Length);
            var end = Math.Clamp(scene.EndOffset, start, source.Length);
            return new SceneToJudge(scene.Number, scene.ChapterLabel, source[start..end],
                SceneContext.StorySoFar(blocks, scene.StartOffset), voice);
        }
    }

    // ---- Batches (owner's ruling, R39) ----------------------------------------------------------------

    // Puts the next batch of fresh scenes' pictures on Gemini's batch tier, within the inline size budget. A
    // scene too large for any batch, or left alone in one, is set aside to go one at a time.
    private async Task<ProcessResult> SubmitImageBatch(
        FolioDbContext db, Novel novel, string source, IReadOnlyList<Scene> fresh, IReadOnlyList<ModelBatch> batches,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = new List<SceneImageJob>();
        foreach (var candidate in fresh)
        {
            var start = Math.Clamp(candidate.StartOffset, 0, source.Length);
            var text = source[start..Math.Clamp(candidate.EndOffset, start, source.Length)];
            jobs.Add(new SceneImageJob(candidate.Number, candidate.ImagePrompt!,
                await LoadSceneReferenceImages(db, novel.Id, text, cancellationToken), PortraitAspectRatio));
        }
        var (chunk, tooLarge) = ImageBatchPlan.Chunk(jobs);
        var byNumber = fresh.ToDictionary(scene => scene.Number);
        foreach (var job in tooLarge)
            byNumber[job.SceneNumber].ImageBatched = true;
        if (chunk.Count < BatchPlan.MinimumItems)
        {
            foreach (var job in chunk)
                byNumber[job.SceneNumber].ImageBatched = true;
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (!geminiLimiter.TryAcquire())
            return ProcessResult.Waiting(geminiLimiter.TimeUntilNext(), novel.Id);

        string? name;
        try
        {
            name = await gemini.SubmitSceneImages(chunk, novel.ArtStyle, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && (NetworkFailure.IsOffline(ex) || ex is RateLimitException))
        {
            var delay = ex is RateLimitException limit ? backoff.NextDelay(0, limit.RetryAfter) : TimeSpan.FromSeconds(rateLimits.OfflineRetrySeconds);
            logger.LogWarning(ex, "Image batch for manuscript {NovelId} not submitted (offline or throttled); trying again later", novel.Id);
            return ProcessResult.Waiting(delay, novel.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Gemini refused the image batch for manuscript {NovelId}; its pictures go one at a time", novel.Id);
            name = null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            db.ModelBatches.Add(new ModelBatch { NovelId = novel.Id, Kind = BatchKinds.SceneImagesOff, SubmittedAtUtc = now, CompletedAtUtc = now });
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        var number = batches.Count(b => b.Kind != BatchKinds.SceneImagesOff) + 1;
        db.ModelBatches.Add(new ModelBatch
        {
            NovelId = novel.Id, Kind = BatchKinds.SceneImagesPrefix + number, BatchId = name, SubmittedAtUtc = now,
        });
        foreach (var job in chunk)
            byNumber[job.SceneNumber].ImageBatched = true;
        logger.LogInformation("Image batch {BatchId} submitted for manuscript {NovelId}: {Count} scenes", name, novel.Id, chunk.Count);
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // Asks whether the open picture batch has ended; when it has, each picture lands on its own scene. Null
    // while it runs. A batch that ended with no pictures at all turns batching off for the draft.
    private async Task<ProcessResult?> PollImageBatch(
        FolioDbContext db, Novel novel, ModelBatch batch, DateTimeOffset now, CancellationToken cancellationToken)
    {
        GeminiBatchPoll poll;
        try
        {
            poll = await gemini.PollSceneImages(batch.BatchId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not ask after image batch {BatchId} for manuscript {NovelId}; asking again later",
                batch.BatchId, novel.Id);
            batch.LastPolledAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        batch.LastPolledAtUtc = now;
        if (!poll.Ended)
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var scenes = await db.Scenes.Where(s => s.NovelId == novel.Id).ToListAsync(cancellationToken);
        var painted = 0;
        foreach (var item in poll.Images.Where(item => item.Value is { Length: > 0 }))
        {
            if (scenes.FirstOrDefault(s => s.Number == item.SceneNumber && s.ImageStatus != WorkStatus.Completed) is not { } painting)
                continue;
            painting.ImagePath = imageStore.Save($"{novel.Id}/scenes/scene-{painting.Number}.png", item.Value!);
            painting.ImageStatus = WorkStatus.Completed;
            painting.LastError = null;
            painted++;
        }
        batch.CompletedAtUtc = now;
        if (painted == 0)
            db.ModelBatches.Add(new ModelBatch { NovelId = novel.Id, Kind = BatchKinds.SceneImagesOff, SubmittedAtUtc = now, CompletedAtUtc = now });
        logger.LogInformation(
            "Image batch {BatchId} for manuscript {NovelId}: {Painted} pictures of {Asked}; the rest go one at a time",
            batch.BatchId, novel.Id, painted, poll.Images.Count);
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private static BatchState? StateOf(ModelBatch? batch) =>
        batch is null ? null : new BatchState(batch.SubmittedAtUtc, batch.LastPolledAtUtc, batch.CompletedAtUtc);

    // Submits one batch and records it. A submit that fails (or returns no id) is recorded as already
    // completed, so this kind goes one call at a time for the draft rather than resubmitting.
    private async Task SubmitBatch(
        FolioDbContext db, Novel novel, string kind, Func<Task<string?>> submit, CancellationToken cancellationToken)
    {
        var batch = new ModelBatch { NovelId = novel.Id, Kind = kind, SubmittedAtUtc = clock.UtcNow };
        try
        {
            batch.BatchId = await submit() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "Could not submit the {Kind} batch for manuscript {NovelId}; going one call at a time: {Error}",
                kind, novel.Id, ErrorText.Describe(ex));
        }

        if (batch.BatchId.Length == 0)
            batch.CompletedAtUtc = clock.UtcNow;
        db.ModelBatches.Add(batch);
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Asks whether a batch has ended, and records the poll. A poll that fails is not an error for the
    // draft: the batch is asked again at the next interval.
    private async Task<bool> PollBatch(FolioDbContext db, ModelBatch batch, CancellationToken cancellationToken)
    {
        bool ended;
        try
        {
            ended = await claude.BatchEnded(batch.BatchId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Could not poll batch {BatchId} ({Kind}); asking again shortly: {Error}",
                batch.BatchId, batch.Kind, ErrorText.Describe(ex));
            ended = false;
        }

        batch.LastPolledAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ended;
    }

    private async Task AbandonBatch(FolioDbContext db, Novel novel, ModelBatch batch, CancellationToken cancellationToken)
    {
        batch.CompletedAtUtc = clock.UtcNow;
        logger.LogWarning(
            "Batch {BatchId} ({Kind}) for manuscript {NovelId} outlived its window; the rest go one call at a time",
            batch.BatchId, batch.Kind, novel.Id);
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DesignBible> LoadBible(FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
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
        FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
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
        FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
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

    private ProcessResult Fail(FolioDbContext db, Novel novel, string message, CancellationToken cancellationToken)
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
        if (stage == ProjectStage.Completed && novel.Regenerating)
        {
            novel.Regenerating = false;
            novel.RegenerationReset = false;
            novel.RegeneratedAtUtc = clock.UtcNow;
        }
        Touch(novel);
    }

    private void Touch(Novel novel) => novel.UpdatedAtUtc = clock.UtcNow;
    /// <summary>
    /// What the report's cover prints. The jacket is read off disk <b>here</b>, once, so the renderer
    /// is handed bytes rather than a path — a page that fetched its own image mid-render could print
    /// the owner's mark in colours taken from a jacket that is no longer the book's (the cover can be
    /// replaced or repainted while a review is in flight).
    ///
    /// <para>A cover that has gone missing is not an error: the slot prints empty and the mark falls
    /// back to the page's own ink. A report is worth more than its cover.</para>
    /// </summary>
    private static ReportCover BuildCover(Novel novel, int wordCount)
    {
        byte[]? jacket = null;
        if (!string.IsNullOrWhiteSpace(novel.CoverPath) && File.Exists(novel.CoverPath))
        {
            jacket = File.ReadAllBytes(novel.CoverPath);
        }

        var series = novel.SeriesId is null || string.IsNullOrWhiteSpace(novel.SeriesName)
            ? null
            : $"Book {novel.SeriesSequence} · {novel.SeriesName}";

        return new ReportCover(
            novel.Title, novel.DraftNumber, wordCount, DateTimeOffset.Now, jacket, series);
    }

    /// <summary>
    /// The next pass this draft has not read yet, in plan order, or null when the read is done.
    ///
    /// <para>Order is the whole point of reading them one at a time from a plan rather than picking
    /// whichever row is unfinished: grouped by model, the manuscript is cached once per model; shuffled,
    /// every change of model pays another cache write.</para>
    /// </summary>
    /// <summary>
    /// Batches the next model's whole-book passes once that model's cache is warm. Returns null when the
    /// next pass should run directly: the first pass of its model, a model whose batch is done, or a
    /// single pass left.
    /// </summary>
    private async Task<ProcessResult?> BatchTheReads(
        FolioDbContext db, Novel novel, BookAnalysis analysis, CancellationToken cancellationToken)
    {
        var done = await db.PassReads
            .Where(pass => pass.NovelId == novel.Id && pass.Json != null)
            .Select(pass => pass.PassId)
            .ToListAsync(cancellationToken);
        var next = ReadingPlan.Passes.FirstOrDefault(pass => !pass.IsSynthesis && !done.Contains(pass.Id));
        if (next is null)
            return null;

        var sameModel = ReadingPlan.Passes.Where(pass => !pass.IsSynthesis && pass.Tier == next.Tier).ToList();
        if (!sameModel.Any(pass => done.Contains(pass.Id)))
            return null; // the first pass of this model runs directly, to write the cache

        var waiting = sameModel.Where(pass => !done.Contains(pass.Id)).ToList();
        var kind = next.Tier == ModelTier.Strong ? BatchKinds.ReadsStrong : BatchKinds.ReadsStandard;
        var batch = await db.ModelBatches.FirstOrDefaultAsync(b => b.NovelId == novel.Id && b.Kind == kind, cancellationToken);
        var (step, wait) = BatchPlan.Decide(waiting.Count, StateOf(batch), clock.UtcNow);
        var personas = waiting.Select(pass => pass.Persona!).ToList();

        switch (step)
        {
            case BatchStep.Submit:
                if (!claudeLimiter.TryAcquire())
                    return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);
                var manuscript = await Manuscript(db, novel.Id, cancellationToken);
                var frame = await Frame(db, novel, analysis, cancellationToken);
                await SubmitBatch(db, novel, kind, () => claude.SubmitReads(personas, manuscript, frame, cancellationToken), cancellationToken);
                return ProcessResult.DidWork(novel.Id);

            case BatchStep.Poll:
                if (!await PollBatch(db, batch!, cancellationToken))
                    return ProcessResult.Waiting(BatchPlan.PollEvery, novel.Id);
                var results = await claude.ReadResults(batch!.BatchId, personas, cancellationToken);
                foreach (var result in results.Where(result => result.Json is not null))
                {
                    var row = await db.PassReads.FirstOrDefaultAsync(
                        pass => pass.NovelId == novel.Id && pass.PassId == result.PersonaId, cancellationToken);
                    if (row is null)
                    {
                        row = new PassRead { NovelId = novel.Id, PassId = result.PersonaId };
                        db.PassReads.Add(row);
                    }
                    row.Json = result.Json;
                    row.CompletedAtUtc = clock.UtcNow;
                    row.LastError = null;
                }
                batch.CompletedAtUtc = clock.UtcNow;
                logger.LogInformation(
                    "Read batch {BatchId} ({Kind}) for manuscript {NovelId}: {Done} of {Count} passes; the rest go one at a time",
                    batch.BatchId, kind, novel.Id, results.Count(result => result.Json is not null), personas.Count);
                Touch(novel);
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);

            case BatchStep.Wait:
                return ProcessResult.Waiting(wait, novel.Id);

            case BatchStep.Abandon:
                await AbandonBatch(db, novel, batch!, cancellationToken);
                return ProcessResult.DidWork(novel.Id);

            default:
                return null;
        }
    }

    private async Task<ReadingPass?> NextUnreadPass(FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var done = await db.PassReads
            .Where(pass => pass.NovelId == novelId && pass.Json != null)
            .Select(pass => pass.PassId)
            .ToListAsync(cancellationToken);

        return ReadingPlan.Passes.FirstOrDefault(pass => !done.Contains(pass.Id));
    }

    /// <summary>
    /// The genre the consolidation read from the text, and its market's usual length, for the
    /// whole-book passes. A label and a range, not anyone's notes, so the readers stay independent.
    /// </summary>
    private static GenreFrame Genre(BookAnalysis analysis)
    {
        if (analysis.ConsolidatedJson is null)
            return GenreFrame.Unknown;

        var report = JsonSerializer.Deserialize<BookAnalysisResult>(analysis.ConsolidatedJson, JsonResponse.CaseInsensitive);
        return report is null ? GenreFrame.Unknown : new GenreFrame(report.Genre ?? string.Empty, report.WordCountRange ?? string.Empty);
    }

    /// <summary>
    /// What every whole-book pass of this draft is told besides the manuscript: the genre, and for a
    /// series book the earlier book's canon (R27). The same for every pass, so the cached prefix holds.
    /// </summary>
    private async Task<ReadingFrame> Frame(FolioDbContext db, Novel novel, BookAnalysis analysis, CancellationToken cancellationToken)
    {
        // The series bible of the other books when there is one (release G), else the earlier book's canon.
        var (_, bible) = await LoadSeriesBible(db, novel, cancellationToken);
        var others = bible.Without(novel.ProjectId);
        if (others.Entries.Count > 0)
            return new(Genre(analysis), BibleText.For(others));

        return new(Genre(analysis), novel.PreviousBookId is { } prior
            ? ReadingFrame.CanonText(await LoadSeriesCanon(db, prior, cancellationToken))
            : string.Empty);
    }

    private async Task<string> Manuscript(FolioDbContext db, Guid novelId, CancellationToken cancellationToken) =>
        await db.SourceTexts.Where(source => source.NovelId == novelId)
            .Select(source => source.FullText).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

    private static async Task<IReadOnlyList<SectionRead>> ReadSections(
        FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var ids = PersonaCatalogue.Sections.Select(persona => persona.Id).ToList();
        var rows = await db.PassReads
            .Where(pass => pass.NovelId == novelId && pass.Json != null && ids.Contains(pass.PassId))
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => JsonSerializer.Deserialize<SectionRead>(row.Json!, JsonResponse.CaseInsensitive))
            .Where(read => read is not null)
            .Select(read => read!)
            .ToList();
    }

    private static async Task<IReadOnlyList<Review>> ReadReaders(
        FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var ids = PersonaCatalogue.Readers.Select(persona => persona.Id).ToList();
        var rows = await db.PassReads
            .Where(pass => pass.NovelId == novelId && pass.Json != null && ids.Contains(pass.PassId))
            .ToListAsync(cancellationToken);

        // Ordered by the catalogue, not by when they finished: the report prints the six in a fixed
        // order and a retry must not move a reader's page.
        return rows
            .Select(row => JsonSerializer.Deserialize<Review>(row.Json!, JsonResponse.CaseInsensitive))
            .Where(review => review is not null)
            .Select(review => review!)
            .OrderBy(review => ids.IndexOf(review.PersonaId))
            .ToList();
    }

    // The two synthesis passes, merged: one carries the revision plan and the other the verdict, and
    // either may not have run yet.
    private static async Task<SynthesisRead> ReadSynthesis(
        FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var ids = ReadingPlan.Synthesis.Select(pass => pass.Id).ToList();
        var rows = await db.PassReads
            .Where(pass => pass.NovelId == novelId && pass.Json != null && ids.Contains(pass.PassId))
            .ToListAsync(cancellationToken);

        var plan = new List<RevisionItem>();
        var fixedItems = new List<RevisionItem>();
        var verdict = Verdict.Empty;
        foreach (var row in rows)
        {
            var read = JsonSerializer.Deserialize<SynthesisRead>(row.Json!, JsonResponse.CaseInsensitive);
            if (read is null)
            {
                continue;
            }
            plan.AddRange(read.RevisionPlan);
            fixedItems.AddRange(read.Fixed ?? []);
            if (!string.IsNullOrWhiteSpace(read.Verdict.MostNeeds)
                || read.Verdict.Strengths is { Count: > 0 } || read.Verdict.Problems is { Count: > 0 })
            {
                verdict = read.Verdict;
            }
        }
        return new SynthesisRead(plan, verdict) { Fixed = fixedItems };
    }

    /// <summary>
    /// The last draft's revision plan, for this draft's plan pass to compare against (R19): its own plan
    /// pass if it ran one, else the plan its consolidation wrote. Empty on a first draft.
    /// </summary>
    private static async Task<IReadOnlyList<RevisionItem>> PreviousPlan(
        FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        if (novel.PreviousDraftId is not { } previousId)
            return [];

        var read = await ReadSynthesis(db, previousId, cancellationToken);
        if (read.RevisionPlan.Count > 0)
            return read.RevisionPlan;

        var stored = await db.BookAnalyses
            .Where(analysis => analysis.NovelId == previousId)
            .Select(analysis => analysis.ConsolidatedJson)
            .FirstOrDefaultAsync(cancellationToken);
        return stored is null
            ? []
            : JsonSerializer.Deserialize<BookAnalysisResult>(stored, JsonResponse.CaseInsensitive)?.RevisionPlan ?? [];
    }

}
