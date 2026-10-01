using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Folio.Domain.Analysis;
using Folio.Domain.Reading;
using Folio.Domain.External;
using Folio.Domain.Imaging;
using Folio.Domain.Model;
using Folio.Domain.Pipeline;
using Folio.Domain.Text;
using Folio.Domain.Time;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.External;
using Folio.Infrastructure.Persistence;
using Storyworthtelling.Paper;
using Storyworthtelling.Voice;

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

    private Task<ProcessResult> Dispatch(FolioDbContext db, Novel novel, CancellationToken cancellationToken) =>
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
            var refined = await voiceClient.RefineAsync(existing, sample, novel.ProjectId, cancellationToken);
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

        // 1) Render one pending/due scene image.
        var scene = await db.Scenes
            .Where(s => s.NovelId == novel.Id &&
                        (s.ImageStatus == WorkStatus.Pending ||
                         (s.ImageStatus == WorkStatus.NeedsRetry && s.NextAttemptAtUtc <= now)))
            .OrderBy(s => s.Number)
            .FirstOrDefaultAsync(cancellationToken);

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
                    return await DeferAfterFailedCall(db, novel, ex, cancellationToken);
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
            novel.LastError = null;
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

            try
            {
                var continuity = await claude.AssessSeriesContinuity(
                    novel.Title, canon, notes, novel.ContinuitySummary, cancellationToken);
                analysis.SeriesContinuityJson = JsonSerializer.Serialize(continuity, JsonResponse.CaseInsensitive);
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

        // 2g) The sixteen reads. Eight whole-book sections on the standard model, then six readers
        // on the strong one, then the two synthesis passes that read the other fourteen.
        //
        // One pass per turn, each persisted the moment it succeeds, because each is a separate model
        // call costing real money: a reader that comes back short must not re-bill the fourteen that
        // were fine. The ORDER is not cosmetic — sections are grouped before readers so the manuscript
        // is written to the cache once per model rather than once per alternation (F50), and the cheap
        // model goes first so a failing key fails cheaply.
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
                            cancellationToken),
                        JsonResponse.CaseInsensitive)
                    : due.Persona!.Kind == PersonaKind.Reader
                        ? JsonSerializer.Serialize(
                            await claude.ReadAsReader(due.Persona, await Manuscript(db, novel.Id, cancellationToken), cancellationToken),
                            JsonResponse.CaseInsensitive)
                        : JsonSerializer.Serialize(
                            await claude.ReadSection(due.Persona, await Manuscript(db, novel.Id, cancellationToken), cancellationToken),
                            JsonResponse.CaseInsensitive);

                row.CompletedAtUtc = clock.UtcNow;
                row.LastError = null;
                novel.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.Attempts++;
                row.LastError = ex.Message;
                await db.SaveChangesAsync(cancellationToken);
                return await DeferAfterFailedCall(db, novel, ex, cancellationToken);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
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
                novel.LastError = null;
            }
            catch (ImageContentBlockedException blocked)
            {
                logger.LogWarning("Manuscript {NovelId} left without a painted jacket after a refusal: {Reason}",
                    novel.Id, blocked.Reason);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await DeferAfterFailedCall(db, novel, ex, cancellationToken);
            }

            novel.CoverAttempted = true;
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
                var expansion = analysis.ExpansionJson is null
                    ? ExpansionReport.Empty
                    : JsonSerializer.Deserialize<ExpansionReport>(analysis.ExpansionJson, JsonResponse.CaseInsensitive) ?? ExpansionReport.Empty;
                var seriesContinuity = analysis.SeriesContinuityJson is null
                    ? SeriesContinuity.Empty
                    : JsonSerializer.Deserialize<SeriesContinuity>(analysis.SeriesContinuityJson, JsonResponse.CaseInsensitive) ?? SeriesContinuity.Empty;

                var wordCount = await WordCount(db, novel.Id, cancellationToken);
                var scenes = await GatherSceneEvidence(db, novel.Id, craft.SceneBreakdown, cancellationToken);
                var review = await BuildDraftReview(db, novel, cancellationToken);
                // The sixteen reads, folded last so the readers replace the omnibus reduce's reviews
                // rather than sitting beside them.
                var sections = await ReadSections(db, novel.Id, cancellationToken);
                var readers = await ReadReaders(db, novel.Id, cancellationToken);
                var synthesis = await ReadSynthesis(db, novel.Id, cancellationToken);

                var folded = report.WithCraft(craft).WithVoiceConsistency(voice).WithWordCount(wordCount)
                    .WithExpansion(expansion).WithSeriesContinuity(seriesContinuity)
                    .WithReads(
                        readers,
                        sections.Where(section => section.HasContent).ToList(),
                        synthesis.RevisionPlan,
                        synthesis.Verdict);
                // The draft's own text, which the report marks the findings onto (Release G). Read
                // here rather than carried through the reduce, because it is the largest thing in the
                // database and every earlier step works from notes about it.
                var manuscript = await db.SourceTexts
                    .Where(source => source.NovelId == novel.Id)
                    .Select(source => source.FullText)
                    .FirstOrDefaultAsync(cancellationToken);

                var pdf = analysisPdfComposer.Compose(
                    folded, BuildCover(novel, wordCount), scenes, review, manuscript);
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
        if (reportReady && archive.DeliverToRemarkable && delivery is { CanDeliverToRemarkable: true } && !analysis!.DeliveredToRemarkable)
        {
            var remarkableResult = await DeliverToRemarkable(db, novel, analysis, cancellationToken);
            if (remarkableResult is { } result)
                return result;
            // null = reMarkable settled (delivered or given up); fall through to the Drive target.
        }

        var reportPending = reportReady && archive.Enabled && delivery is { CanArchiveToDrive: true } && analysis!.ArchivedDriveFileId is null;

        if (!reportPending)
            return await FinishArchiving(db, novel, cancellationToken);

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
        try
        {
            await delivery!.DeliverToRemarkableRoot(
                DeliveredDocument.Name(novel.Title, novel.DraftNumber),
                imageStore.Read(analysis.PdfPath!), cancellationToken);

            analysis.DeliveredToRemarkable = true;
            novel.StageAttempts = 0;
            novel.LastError = null;
            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Delivered the revision report of manuscript {NovelId} to the reMarkable root", novel.Id);
            return ProcessResult.DidWork(novel.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            novel.StageAttempts++;
            novel.LastError = $"Could not deliver to reMarkable: {ex.Message}";
            if (novel.StageAttempts >= rateLimits.MaxAttempts)
            {
                // Give up so the review can still finish (the report is on the device and, if enabled, Drive).
                // Nothing reached the tablet — logged distinctly from a success so diagnostics show the gap.
                logger.LogWarning(ex,
                    "Manuscript {NovelId}: giving up on reMarkable delivery after {Attempts} attempts; nothing " +
                    "was delivered to the tablet, continuing (the report remains available elsewhere)",
                    novel.Id, novel.StageAttempts);
                analysis.DeliveredToRemarkable = true;   // settled
                novel.StageAttempts = 0;                 // hand a fresh budget to the Drive target
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }

            var delay = backoff.NextDelay(novel.StageAttempts - 1, (ex as RateLimitException)?.RetryAfter);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.Waiting(delay, novel.Id);
        }
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

    // Records a failed once-per-manuscript Claude call (a report consolidation) and parks the manuscript
    // for a backoff window instead of letting the failure escape and fail the whole worker cycle. There is
    // no per-unit attempt counter to hang the schedule off here — the unit IS the manuscript — so the delay
    // is the base one, or whatever the provider asked us to wait.
    private async Task<ProcessResult> DeferAfterFailedCall(
        FolioDbContext db, Novel novel, Exception exception, CancellationToken cancellationToken)
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

        return new DraftReview(novel.DraftNumber, findings, resolved);
    }

    // Builds each scene's evidence for the report: the rendered image (if any), cited "Ch. N · Scene M",
    // paired by reading order with the craft read's scene-by-scene note for its purpose and verdict.
    private async Task<IReadOnlyList<SceneEvidence>> GatherSceneEvidence(
        FolioDbContext db, Guid novelId, IReadOnlyList<SceneBeat> sceneBreakdown, CancellationToken cancellationToken)
    {
        var scenes = await db.Scenes
            .Where(s => s.NovelId == novelId)
            .OrderBy(s => s.Number)
            .Select(s => new { s.Number, s.ChapterNumber, s.ChapterLabel, s.ImagePath, s.Changed, s.PreviousSceneId })
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
                .Select(s => new { s.Id, s.ImagePath })
                .ToDictionaryAsync(s => s.Id, s => s.ImagePath, cancellationToken);

        var evidence = new List<SceneEvidence>(scenes.Count);
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var citation = string.IsNullOrWhiteSpace(scene.ChapterLabel)
                ? $"Scene {scene.Number}"
                : $"{scene.ChapterLabel} · Scene {scene.Number}";
            var beat = i < sceneBreakdown.Count ? sceneBreakdown[i] : null;
            byte[]? Read(string? path) =>
                path is not null && imageStore.Exists(path) ? imageStore.Read(path) : null;

            byte[]? previous = null;
            if (scene.Changed && scene.PreviousSceneId is { } priorId
                && priorPaths.TryGetValue(priorId, out var priorPath))
            {
                previous = Read(priorPath);
            }

            evidence.Add(new SceneEvidence(
                citation, beat?.Purpose ?? string.Empty, beat?.Verdict ?? string.Empty,
                Read(scene.ImagePath), previous));
        }
        return evidence;
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
    private async Task<ReadingPass?> NextUnreadPass(FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var done = await db.PassReads
            .Where(pass => pass.NovelId == novelId && pass.Json != null)
            .Select(pass => pass.PassId)
            .ToListAsync(cancellationToken);

        return ReadingPlan.Passes.FirstOrDefault(pass => !done.Contains(pass.Id));
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
        var verdict = Verdict.Empty;
        foreach (var row in rows)
        {
            var read = JsonSerializer.Deserialize<SynthesisRead>(row.Json!, JsonResponse.CaseInsensitive);
            if (read is null)
            {
                continue;
            }
            plan.AddRange(read.RevisionPlan);
            if (!string.IsNullOrWhiteSpace(read.Verdict.MostNeeds)
                || read.Verdict.Strengths is { Count: > 0 } || read.Verdict.Problems is { Count: > 0 })
            {
                verdict = read.Verdict;
            }
        }
        return new SynthesisRead(plan, verdict);
    }

}
