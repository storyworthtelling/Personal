using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.Design;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Domain.Text;
using Storyboarder.Domain.Time;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.External;
using Storyboarder.Infrastructure.Imaging;
using Storyboarder.Infrastructure.Persistence;

namespace Storyboarder.Infrastructure.Pipeline;

// The heart of the app: advances one novel by one unit of work per call, idempotently and resumably.
// State lives entirely in the database, so the worker resumes after pauses, rate limits, or restarts
// simply by querying the next due unit. A single worker calls ProcessNextAsync in a loop.
public sealed class GraphicNovelProcessor : IPipelineStep
{
    private readonly IDbContextFactory<StoryboarderDbContext> dbContextFactory;
    private readonly IClaudeAnalysisClient claude;
    private readonly IGeminiImageClient gemini;
    private readonly IImageStore imageStore;
    private readonly IPageComposer pageComposer;
    private readonly IVolumePdfComposer pdfComposer;
    private readonly IAnalysisPdfComposer analysisPdfComposer;
    private readonly IClock clock;
    private readonly ILogger<GraphicNovelProcessor> logger;

    private readonly RequestRateLimiter claudeLimiter;
    private readonly RequestRateLimiter geminiLimiter;
    private readonly BackoffScheduler backoff;
    private readonly RateLimitOptions rateLimits;
    private readonly PageLayoutOptions pageLayout;
    private readonly TextChunker chunker = new();
    // A finer chunker used only for storyboarding: it slices each (large) design-bible chunk into small
    // sub-windows so a single storyboard Claude call stays short (see RateLimitOptions.StoryboardWindowChars).
    private readonly TextChunker storyboardChunker;
    private readonly ChapterDetector chapterDetector = new();
    private readonly DesignBibleMerger merger = new();
    private readonly CoverPageRenderer coverRenderer = new();
    private readonly PlaceholderPanelRenderer placeholderRenderer = new();

    public GraphicNovelProcessor(
        IDbContextFactory<StoryboarderDbContext> dbContextFactory,
        IClaudeAnalysisClient claude,
        IGeminiImageClient gemini,
        IImageStore imageStore,
        IPageComposer pageComposer,
        IVolumePdfComposer pdfComposer,
        IAnalysisPdfComposer analysisPdfComposer,
        IClock clock,
        IOptions<RateLimitOptions> rateLimitOptions,
        IOptions<PageLayoutOptions> pageLayoutOptions,
        ILogger<GraphicNovelProcessor> logger)
    {
        this.dbContextFactory = dbContextFactory;
        this.claude = claude;
        this.gemini = gemini;
        this.imageStore = imageStore;
        this.pageComposer = pageComposer;
        this.pdfComposer = pdfComposer;
        this.analysisPdfComposer = analysisPdfComposer;
        this.clock = clock;
        this.logger = logger;

        rateLimits = rateLimitOptions.Value;
        pageLayout = pageLayoutOptions.Value;
        storyboardChunker = new TextChunker(rateLimits.StoryboardWindowChars, rateLimits.StoryboardWindowMaxChars);
        claudeLimiter = new RequestRateLimiter(rateLimits.ClaudeRequestsPerMinute, rateLimits.ClaudeRequestsPerDay, clock);
        geminiLimiter = new RequestRateLimiter(rateLimits.GeminiRequestsPerMinute, rateLimits.GeminiRequestsPerDay, clock);
        backoff = new BackoffScheduler(
            TimeSpan.FromSeconds(rateLimits.BaseDelaySeconds), TimeSpan.FromSeconds(rateLimits.MaxDelaySeconds));
    }

    public async Task<ProcessResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Advance the oldest active novel that actually has runnable work. A novel that can't make
        // progress right now must not hog the worker (and stall every novel behind it): that's a novel
        // returning Idle — blocked on a failed unit or parked awaiting the user's approval — but equally
        // one returning Waiting, parked on a rate-limit or retry window. Both are stepped past so a novel
        // with real work gets its turn. This is what keeps a finished-but-one-transition novel (e.g. its
        // analysis PDF is done, only the move to Completed remains) from being starved behind another
        // novel sitting on a long rate-limit wait. Only if no novel can do work do we return: the soonest
        // Waiting if any is merely deferred, else Idle.
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
            ProjectStage.Storyboarding => DoStoryboard(db, novel, cancellationToken),
            ProjectStage.RenderingImages => RenderPanels(db, novel, cancellationToken),
            ProjectStage.ComposingPages => ComposePages(db, novel, cancellationToken),
            ProjectStage.AssemblingVolumes => AssembleVolumes(db, novel, cancellationToken),
            ProjectStage.ComposingAnalysis => ComposeAnalysis(db, novel, cancellationToken),
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
            return Fail(db, novel, "No source text was stored for this novel.", cancellationToken);

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

        SetStage(novel, ProjectStage.BuildingDesignBible);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
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
                logger.LogInformation("Analysed chunk {Ordinal} for the design bible (novel {NovelId})", chunk.Ordinal, novel.Id);
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

        if (await PendingWork(db.Chunks.Where(c => c.NovelId == novel.Id), c => c.AnalysisStatus, c => c.NextAttemptAtUtc, now)
                is { } chunkWait)
            return chunkWait == TimeSpan.MinValue ? ProcessResult.Idle : ProcessResult.Waiting(chunkWait, novel.Id);

        // 2) All chunks analyzed: consolidate the bible exactly once.
        if (!novel.DesignBibleConsolidated)
        {
            await ConsolidateBible(db, novel, cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 3) Render one pending/due reference sheet — unless this is a report-only draft, which draws
        // no art at all. Reference sheets exist solely to keep the panels consistent, and they are the
        // expensive half of this stage (one image call per bible entity), so a report-only run skips
        // them outright rather than paying to illustrate a book that will never be illustrated.
        var reference = novel.ReportOnly ? null : await db.DesignRefs
            .Where(r => r.NovelId == novel.Id &&
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
                logger.LogInformation("Rendered reference sheet for {Kind} '{Name}' (novel {NovelId})", reference.Kind, reference.Name, novel.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any failure — a rate limit, an HTTP error, or a response that carried no usable
                // image (a text-only or safety-blocked reply, which surfaces as a plain exception) —
                // is retried with backoff and, once non-transient attempts run out, marked Failed.
                // Letting it escape here would crash the whole drain cycle and re-attempt the same
                // sheet forever, wedging the novel at "sheets 0 of N" (and every novel queued behind it).
                ScheduleRetry(reference, ex, novel);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // Reference sheets are optional consistency aids — panels render with whatever sheets exist —
        // so a permanently-failed sheet must not block the stage. Only sheets still awaiting a retry
        // hold us here; once none remain Pending/NeedsRetry, advance even if some ended up Failed.
        if (!novel.ReportOnly)
        {
            var pendingSheetRetries = await db.DesignRefs
                .Where(r => r.NovelId == novel.Id && r.ReferenceSheetStatus == WorkStatus.NeedsRetry)
                .Select(r => r.NextAttemptAtUtc)
                .ToListAsync(cancellationToken);
            if (pendingSheetRetries.Count > 0)
                return ProcessResult.Waiting((pendingSheetRetries.Min() ?? now) - now, novel.Id);
        }

        // 4) Everything ready: advance. A report-only draft goes straight to the revision report from
        // here; every other novel goes on to storyboarding (see ProjectStateMachine).
        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, AllChunksAnalyzed: true, BibleConsolidated: true, AllReferenceSheetsRendered: true,
            false, false, false, false, ReportOnly: novel.ReportOnly)));
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

        foreach (var character in bible.Characters)
            db.DesignRefs.Add(NewDesignRef(novel.Id, ReferenceKind.Character, character));
        foreach (var setting in bible.Settings)
            db.DesignRefs.Add(NewDesignRef(novel.Id, ReferenceKind.Setting, setting));

        novel.DesignBibleConsolidated = true;
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProcessResult> DoStoryboard(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var chunkCount = await db.Chunks.CountAsync(c => c.NovelId == novel.Id, cancellationToken);
        if (novel.StoryboardCursor >= chunkCount)
        {
            SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
                novel.Stage, false, false, false, false, StoryboardCreated: true, false, false, false)));
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // Bounded retries: once the current window has failed MaxAttempts times, stop re-calling Claude
        // (every attempt bills tokens) and leave the novel stalled with its error until a manual retry
        // resets the counter. Without this a persistently-failing call retries — and re-bills — forever.
        if (novel.StoryboardAttempts >= rateLimits.MaxAttempts)
            return ProcessResult.Idle;

        var chunkText = await db.Chunks
            .Where(c => c.NovelId == novel.Id && c.Ordinal == novel.StoryboardCursor)
            .Select(c => c.Text)
            .FirstAsync(cancellationToken);

        // Adapt the chunk one small sub-window at a time. A whole ~48K-char chunk in a single call runs
        // for many minutes — long enough that Android's background worker is killed mid-stream. Slicing
        // it into small, boundary-aligned windows keeps every call short, and persisting the sub-window
        // index makes it resumable across worker cycles.
        var windows = storyboardChunker.Chunk(chunkText);

        // A blank/whitespace chunk yields no windows (or the index has already reached the end): the
        // chunk is done, so advance the cursor without billing a Claude call.
        if (novel.StoryboardWindowIndex >= windows.Count)
        {
            novel.StoryboardCursor++;
            novel.StoryboardWindowIndex = 0;
            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (!claudeLimiter.TryAcquire())
            return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

        var bible = await LoadBible(db, novel.Id, cancellationToken);
        var window = windows[novel.StoryboardWindowIndex].Text;

        StoryboardSegment segment;
        try
        {
            segment = await claude.Storyboard(window, novel.ContinuitySummary, bible, novel.ArtStyle, cancellationToken);
        }
        // A transient failure here (rate limit, HTTP error, or a network drop like "Socket closed")
        // is retried with backoff; letting it escape would fail the whole worker cycle.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            novel.StoryboardAttempts++;
            novel.LastError = ex.Message;
            var retryAfter = (ex as RateLimitException)?.RetryAfter;
            var delay = backoff.NextDelay(novel.StoryboardAttempts - 1, retryAfter);
            if (ex is RateLimitException)
                novel.RateLimitedUntilUtc = clock.UtcNow + delay;
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.Waiting(delay, novel.Id);
        }

        // Resolve the novel's existing volumes (id + current page count) WITHOUT tracking them. Once
        // storyboarding reuses a volume across windows/chunks, loading that volume graph and appending
        // to it while also updating the novel trips SaveChanges' concurrency check; inserting new rows
        // against explicit foreign keys keeps the change-tracker to just the novel plus the new inserts.
        var existing = (await db.Volumes
                .Where(v => v.NovelId == novel.Id)
                .Select(v => new { v.Id, v.Number, PageCount = v.Pages.Count })
                .ToListAsync(cancellationToken))
            .ToDictionary(v => v.Number, v => (v.Id, PageCount: v.PageCount));

        foreach (var volumeDraft in segment.Volumes)
        {
            if (!existing.TryGetValue(volumeDraft.Number, out var target))
            {
                var volume = new Volume { NovelId = novel.Id, Number = volumeDraft.Number, Title = volumeDraft.Title };
                db.Volumes.Add(volume);
                target = (volume.Id, 0);
            }

            foreach (var pageDraft in volumeDraft.Pages)
            {
                // The model is steered toward 3-6 panels per page, but a page it returns with more than
                // the cap is split across several physical pages (panels distributed as evenly as
                // possible) so panels are never squished into a dense grid.
                foreach (var panelGroup in SplitForPages(pageDraft.Panels, pageLayout.MaxPanelsPerPage))
                {
                    var page = new Page
                    {
                        VolumeId = target.Id,
                        ChapterNumber = pageDraft.ChapterNumber,
                        PageNumber = ++target.PageCount
                    };
                    int panelNumber = 1;
                    foreach (var panelDraft in panelGroup)
                        page.Panels.Add(new Panel
                        {
                            PageId = page.Id,
                            PanelNumber = panelNumber++,
                            VisualPrompt = panelDraft.VisualPrompt,
                            Dialogue = panelDraft.Dialogue,
                            Caption = panelDraft.Caption,
                            ReferenceNamesJson = JsonSerializer.Serialize(panelDraft.ReferenceNames, JsonResponse.CaseInsensitive)
                        });
                    db.Pages.Add(page);
                }
            }

            // Remember the running page count so a volume number repeated later in this segment keeps numbering.
            existing[volumeDraft.Number] = target;
        }

        novel.ContinuitySummary = segment.ContinuitySummary;
        novel.StoryboardWindowIndex++;
        // Chunk finished once its last window is adapted: advance to the next chunk and reset the
        // sub-window index. The outer cursor check ends storyboarding when the last chunk is done.
        if (novel.StoryboardWindowIndex >= windows.Count)
        {
            novel.StoryboardCursor++;
            novel.StoryboardWindowIndex = 0;
        }
        novel.StoryboardAttempts = 0;
        novel.LastError = null;
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task<ProcessResult> RenderPanels(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var volumeIds = await db.Volumes.Where(v => v.NovelId == novel.Id).Select(v => v.Id).ToListAsync(cancellationToken);
        var pageIds = await db.Pages.Where(pg => volumeIds.Contains(pg.VolumeId)).Select(pg => pg.Id).ToListAsync(cancellationToken);
        var panelsForNovel = db.Panels.Where(p => pageIds.Contains(p.PageId));

        var panel = await panelsForNovel
            .Where(p => p.Status == WorkStatus.Pending ||
                        (p.Status == WorkStatus.NeedsRetry && p.NextAttemptAtUtc <= now))
            .OrderBy(p => p.PanelNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (panel is not null)
        {
            if (!geminiLimiter.TryAcquire())
                return ProcessResult.Waiting(geminiLimiter.TimeUntilNext(), novel.Id);

            try
            {
                var references = await LoadReferenceImages(db, novel.Id, panel.ReferenceNamesJson, cancellationToken);
                // Request the image at the aspect ratio of the slot this panel will occupy on the page,
                // so it fills the panel rather than being letterboxed onto shaded paper when composed.
                var aspectRatio = await PanelSlotAspectRatio(db, panel, cancellationToken);
                // An approved panel renders with ONLY its flagged categories relaxed, for this one call;
                // every other panel takes the default render (and the app's default thresholds) untouched.
                var relaxed = panel.ApprovedForRelaxedRender
                    ? JsonSerializer.Deserialize<List<string>>(panel.BlockedCategoriesJson ?? "[]") ?? []
                    : [];
                var image = relaxed.Count > 0
                    ? await gemini.GeneratePanel(panel.VisualPrompt, novel.ArtStyle, references, aspectRatio, relaxed, cancellationToken)
                    : await gemini.GeneratePanel(panel.VisualPrompt, novel.ArtStyle, references, aspectRatio, cancellationToken);
                panel.ImagePath = imageStore.Save($"{novel.Id}/panels/{panel.Id}.png", image);
                panel.Status = WorkStatus.Completed;
                panel.LastError = null;
                logger.LogInformation("Rendered panel {PanelId} (novel {NovelId})", panel.Id, novel.Id);
            }
            // A deliberate content block is handled distinctly from a transient failure: Prohibited (IP /
            // copyrighted content) cancels the whole book; Safety parks the panel for the user's approval,
            // then — once approved — relaxes the category, and if that still hits the model's floor, has
            // Claude restage the prompt to imply the content before finally falling back to a placeholder;
            // Filtered (Gemini's IMAGE_SAFETY image filter) takes that same restage-then-placeholder path
            // straight away, since it can't be relaxed and shouldn't cancel the book.
            catch (ImageContentBlockedException blocked)
            {
                if (await HandleBlockedPanel(novel, panel, blocked, cancellationToken) is { } deferral)
                    return deferral;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // As with reference sheets: a rate limit, HTTP error, or no-image response is retried
                // with backoff (then Failed once attempts run out) rather than escaping and crashing
                // the cycle. A permanently-failed panel still blocks page composition below, by design.
                ScheduleRetry(panel, ex, novel);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (await PendingWork(panelsForNovel, p => p.Status, p => p.NextAttemptAtUtc, now) is { } wait)
            return wait == TimeSpan.MinValue ? ProcessResult.Idle : ProcessResult.Waiting(wait, novel.Id);

        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, false, false, false, false, AllPanelsRendered: true, false, false)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task<ProcessResult> ComposePages(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        // Self-heal: a Completed panel whose image file has vanished (cache eviction, a partial
        // restore) would make composition throw on the missing file and block the novel forever;
        // send it back to be re-rendered instead.
        if (await RevertPanelsWithMissingImages(db, novel, cancellationToken))
            return ProcessResult.DidWork(novel.Id);

        var volumeIds = await db.Volumes.Where(v => v.NovelId == novel.Id).Select(v => v.Id).ToListAsync(cancellationToken);

        var page = await db.Pages
            .Where(p => volumeIds.Contains(p.VolumeId) && p.CompositionStatus == WorkStatus.Pending)
            .Include(p => p.Panels)
            .OrderBy(p => p.PageNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (page is not null)
        {
            try
            {
                // Skipped panels (the user chose to leave them out) are excluded, so the page is drawn
                // without them. If every panel on the page was skipped, fall back to a single placeholder
                // so the composer still has something to lay out rather than throwing on an empty page.
                var panelArt = page.Panels
                    .Where(p => !p.Skipped)
                    .OrderBy(p => p.PanelNumber)
                    .Select(p => new PanelArt(imageStore.Read(p.ImagePath!), p.Dialogue, p.Caption))
                    .ToList();
                if (panelArt.Count == 0)
                    panelArt.Add(new PanelArt(placeholderRenderer.Render(), null, null));
                var volume = await db.Volumes.FirstAsync(v => v.Id == page.VolumeId, cancellationToken);
                var chrome = new PageChrome(page.PageNumber, $"{novel.Title} · Volume {volume.Number}");
                var composed = pageComposer.Compose(panelArt, chrome);
                page.ComposedImagePath = imageStore.Save($"{novel.Id}/pages/{page.Id}.png", composed);
                page.ThumbImagePath = imageStore.Save(
                    $"{novel.Id}/pages/{page.Id}.thumb.png", Thumbnailer.Downscale(composed, 256));
                page.CompositionStatus = WorkStatus.Completed;
                page.LastError = null;
            }
            catch (Exception ex)
            {
                page.Attempts++;
                page.LastError = ex.Message;
                page.CompositionStatus = WorkStatus.Failed;
                logger.LogError(ex, "Failed to compose page {PageId}", page.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (await db.Pages.AnyAsync(p => volumeIds.Contains(p.VolumeId) && p.CompositionStatus != WorkStatus.Completed, cancellationToken))
            return ProcessResult.Idle; // blocked on a failed page

        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, false, false, false, false, false, AllPagesComposed: true, false)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task<ProcessResult> AssembleVolumes(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        // Self-heal: a Completed page whose composed image has vanished would make PDF assembly throw;
        // send it back to be recomposed (which in turn re-renders any missing panels) instead.
        if (await RevertPagesWithMissingImages(db, novel, cancellationToken))
            return ProcessResult.DidWork(novel.Id);

        var now = clock.UtcNow;
        var volume = await db.Volumes
            .Where(v => v.NovelId == novel.Id &&
                        (v.PdfStatus == WorkStatus.Pending ||
                         (v.PdfStatus == WorkStatus.NeedsRetry && v.NextAttemptAtUtc <= now)))
            .Include(v => v.Pages)
            .OrderBy(v => v.Number)
            .FirstOrDefaultAsync(cancellationToken);

        if (volume is not null)
        {
            try
            {
                var pages = volume.Pages.OrderBy(p => p.PageNumber).ToList();

                // Assemble one page per call so only a single page raster is ever held in memory, and so
                // a hang or restart resumes from PdfPageCursor instead of rebuilding the whole volume in
                // one memory-heavy pass. PdfPageCursor counts the volume pages already appended; the
                // cover is written once, as the first page, when the cursor is still 0.
                cancellationToken.ThrowIfCancellationRequested();

                var pdf = volume.PdfPageCursor == 0
                    ? pdfComposer.AppendPage(null, coverRenderer.VolumeCover(novel.Title, volume.Number, volume.Title), DesignTokens.Comic.PrintDpi)
                    : imageStore.Read(volume.PdfPath!);

                if (volume.PdfPageCursor < pages.Count)
                {
                    var pageImage = imageStore.Read(pages[volume.PdfPageCursor].ComposedImagePath!);
                    pdf = pdfComposer.AppendPage(pdf, pageImage, DesignTokens.Comic.PrintDpi);
                    volume.PdfPageCursor++;
                }

                volume.PdfPath = imageStore.Save($"{novel.Id}/volumes/volume-{volume.Number}.pdf", pdf);

                // A page appended cleanly: clear any earlier transient-failure state so a hiccup partway
                // through a long volume doesn't count against the whole binding.
                volume.Attempts = 0;
                volume.LastError = null;
                volume.NextAttemptAtUtc = null;

                if (volume.PdfPageCursor >= pages.Count)
                {
                    volume.PdfStatus = WorkStatus.Completed;
                    logger.LogInformation("Assembled volume {Number} of novel {NovelId} ({Pages} pages)",
                        volume.Number, novel.Id, pages.Count);
                }
                else
                {
                    logger.LogInformation("Assembling volume {Number} of novel {NovelId}: page {Cursor}/{Total}",
                        volume.Number, novel.Id, volume.PdfPageCursor, pages.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A partial PDF may be corrupt, so restart this volume's assembly from scratch rather
                // than resuming a bad file, and retry with backoff (Failed only after MaxAttempts) so a
                // single throw no longer pins the novel at AssemblingVolumes forever.
                volume.PdfPageCursor = 0;
                volume.PdfPath = null;
                ScheduleRetry(volume, ex, novel);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // Volumes still pending a retry hold the stage (and report when they resume); once only Completed
        // (or permanently-Failed) volumes remain, PendingWork stops waiting. A Failed volume blocks the
        // stage until a manual retry revives it — surfaced to the user, who can redo the binding.
        if (await PendingWork(db.Volumes.Where(v => v.NovelId == novel.Id), v => v.PdfStatus, v => v.NextAttemptAtUtc, now)
                is { } volumeWait)
            return volumeWait == TimeSpan.MinValue ? ProcessResult.Idle : ProcessResult.Waiting(volumeWait, novel.Id);

        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, false, false, false, false, false, false, AllVolumesAssembled: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // Builds the book-analysis (revision report) PDF: map each chunk to notes, consolidate once, then
    // render. Structured like BuildDesignBible — one unit of work per call, fully resumable from the DB.
    private async Task<ProcessResult> ComposeAnalysis(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // 0) The report PDF is already on disk, so this stage is finished: advance and do nothing else.
        // This has to come FIRST, ahead of the labelling and seeding below, because none of that
        // preparatory work can change a report that is already written: an existing PDF cannot gain
        // chapter citations, and a report rendered before the craft half of the reduce existed carries
        // CraftJson = null, which would otherwise buy a consolidation whose output could never reach it.
        var rendered = await db.BookAnalyses
            .AnyAsync(a => a.NovelId == novel.Id && a.PdfStatus == WorkStatus.Completed, cancellationToken);
        if (rendered)
            return await AdvancePastAnalysis(db, novel, cancellationToken);

        // 0a) Every finding in the report cites a chapter, which the map step reads off its chunk. A
        // conversion chunked before chapter detection existed still has unlabelled chunks, so label
        // them here rather than producing a report with no citations. A no-op once settled.
        if (await EnsureChaptersLabelled(db, novel, fullText: null, cancellationToken))
            return ProcessResult.DidWork(novel.Id);

        // 0b) Seed one analysis unit per chunk (once), plus the per-novel analysis record.
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
            // Retry any transient failure (rate limit, HTTP error, or a network drop like "Socket
            // closed") with backoff instead of letting it escape and fail the whole worker cycle.
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
        // Step 0 already returned for an already-rendered report, so reaching here means the PDF is
        // still outstanding and both reduce halves are worth paying for.
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
            // Retry any transient failure (rate limit, HTTP error, or a network drop like "Socket
            // closed") with backoff instead of letting it escape and fail the whole worker cycle.
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

        // 3) Render: compose the analysis PDF from the consolidated report (both halves folded into one).
        if (analysis.PdfStatus != WorkStatus.Completed)
        {
            try
            {
                var report = JsonSerializer.Deserialize<BookAnalysisResult>(analysis.ConsolidatedJson, JsonResponse.CaseInsensitive)
                             ?? BookAnalysisResult.Empty;
                var craft = analysis.CraftJson is null
                    ? CraftReport.Empty
                    : JsonSerializer.Deserialize<CraftReport>(analysis.CraftJson, JsonResponse.CaseInsensitive) ?? CraftReport.Empty;
                var pdf = analysisPdfComposer.Compose(report.WithCraft(craft), novel.Title);
                analysis.PdfPath = imageStore.Save($"{novel.Id}/analysis/book-analysis.pdf", pdf);
                analysis.PdfStatus = WorkStatus.Completed;
            }
            // A failed render is left for the next cycle to retry; don't advance on it.
            catch (Exception ex)
            {
                analysis.PdfStatus = WorkStatus.Failed;
                logger.LogError(ex, "Failed to compose analysis PDF for novel {NovelId}", novel.Id);
                await db.SaveChangesAsync(cancellationToken);
                return ProcessResult.DidWork(novel.Id);
            }
        }

        // 4) The report is rendered — either just now or on an earlier cycle — so the book is finished.
        return await AdvancePastAnalysis(db, novel, cancellationToken);
    }

    // Moves a novel off ComposingAnalysis now that its revision report exists. Shared by the two paths
    // that get there: a report rendered on this cycle, and one that was already on disk when the stage
    // was entered (step 0).
    private async Task<ProcessResult> AdvancePastAnalysis(
        StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, SourceIngested: false, AllChunksAnalyzed: false, BibleConsolidated: false,
            AllReferenceSheetsRendered: false, StoryboardCreated: false, AllPanelsRendered: false,
            AllPagesComposed: false, AllVolumesAssembled: false, AnalysisComplete: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // --- helpers ---

    // Records a failed once-per-novel Claude call (a report consolidation) and parks the novel for a
    // backoff window instead of letting the failure escape and fail the whole worker cycle. There is
    // no per-unit attempt counter to hang the schedule off here — the unit IS the novel — so the delay
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
    // finding drawn from that chunk can cite where in the book it came from. Idempotent and cheap
    // once settled: a single indexed query. Also heals conversions that were chunked before chapter
    // detection existed — they reach the report with unlabelled chunks otherwise. Detection itself is
    // deterministic (ChapterDetector), so re-running it on the same text would give the same spans.
    // Returns whether anything changed (the caller treats that as one unit of work).
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
                "Detected {Count} chapter(s) in novel {NovelId}", spans.Count, novel.Id);
        }

        // No spans means the chapter rows are gone under a novel already marked as detected — nothing
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

    // Reacts to a panel render the provider blocked on content grounds. Returns a ProcessResult for the
    // caller to return immediately (only when the restage needs a Claude slot that isn't free yet), or
    // null to let the caller persist the mutated state and report DidWork.
    private async Task<ProcessResult?> HandleBlockedPanel(
        Novel novel, Panel panel, ImageContentBlockedException blocked, CancellationToken cancellationToken)
    {
        if (blocked.Reason == ImageBlockReason.Prohibited && panel.PromptRewrittenForSafety)
        {
            // Restaged to imply the beat and STILL refused as prohibited/IP content: rewording could not
            // get around it, so this panel needs the owner. Park the PANEL, never the book — cancelling
            // the whole conversion over one panel is what killed two of the owner's novels after hours of
            // rendering. The render query skips AwaitingApproval, so every other panel keeps rendering;
            // the book simply can't finalize until this one is reworked or left out.
            //
            // No BlockedCategoriesJson: prohibited content is not an adjustable harm category, so there is
            // nothing for an "approve and relax" to act on. The review UI offers rework-or-skip instead.
            // Settle it with no retry schedule so the rework lookup finds it deterministically.
            panel.Status = WorkStatus.AwaitingApproval;
            panel.NextAttemptAtUtc = null;
            panel.BlockedCategoriesJson = null;
            panel.LastError = blocked.Message;
            // Capture the exact prompt that tripped the filter alongside the block, so the blocked-panel
            // diagnostic (and the log) carry what's needed to rework the art direction — not just the refusal.
            logger.LogWarning(
                "Panel {PanelId} (#{PanelNumber}) still refused as prohibited content after restaging; " +
                "awaiting the user (novel {NovelId} continues). Prompt sent to Gemini: {Prompt}. Provider reason: {Error}",
                panel.Id, panel.PanelNumber, novel.Id, ReconstructPanelPrompt(novel, panel), blocked.Message);
            return null;
        }

        if (blocked.Reason == ImageBlockReason.Safety && !panel.ApprovedForRelaxedRender)
        {
            // First safety block: park it for the user's decision and move on. The render query skips
            // AwaitingApproval panels, so the rest of the book keeps rendering; the novel simply can't
            // finalize until the user approves or skips this panel. (A Filtered block skips this gate —
            // relaxing can't lift Gemini's image filter and there's nothing to usefully approve — and
            // goes straight to restaging below.)
            panel.Status = WorkStatus.AwaitingApproval;
            panel.BlockedCategoriesJson = JsonSerializer.Serialize(blocked.Categories, JsonResponse.CaseInsensitive);
            panel.LastError = blocked.Message;
            logger.LogInformation(
                "Panel {PanelId} (#{PanelNumber}) safety-blocked ({Categories}); awaiting user approval (novel {NovelId}). " +
                "Prompt sent to Gemini: {Prompt}",
                panel.Id, panel.PanelNumber, string.Join(", ", blocked.Categories), novel.Id,
                ReconstructPanelPrompt(novel, panel));
            return null;
        }

        if (!panel.PromptRewrittenForSafety)
        {
            // Still blocked past what relaxing can reach — an approved Safety render at the model's floor,
            // a Filtered (IMAGE_SAFETY) block, which can't be relaxed at all, or a first Prohibited block,
            // where rewording is the only lever that can help. Ask Claude to restage the beat so the
            // content is implied, then re-render the reworked prompt next cycle.
            if (!claudeLimiter.TryAcquire())
                return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);
            try
            {
                panel.VisualPrompt = await claude.RewritePromptToImply(panel.VisualPrompt, cancellationToken);
                panel.PromptRewrittenForSafety = true;
                panel.LastError = null;
                logger.LogInformation("Panel {PanelId} restaged to imply blocked content (novel {NovelId})", panel.Id, novel.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Couldn't restage right now (rate limit / transient): retry it later with backoff.
                ScheduleRetry(panel, ex, novel);
            }
            return null;
        }

        // Restaged to imply — and still blocked (a Safety render past relax, or an unshakable Filtered
        // one). Final graceful fallback so the book finishes: a neutral placeholder tile (the panel's
        // dialogue/caption are still typeset over it).
        panel.ImagePath = imageStore.Save($"{novel.Id}/panels/{panel.Id}.png", placeholderRenderer.Render());
        panel.Status = WorkStatus.Completed;
        panel.LastError = "Rendered as a neutral placeholder: blocked even after relaxing safety and restaging to imply the content.";
        logger.LogWarning("Panel {PanelId} fell back to a placeholder after repeated content blocks (novel {NovelId})", panel.Id, novel.Id);
        return null;
    }

    // The width/height of the ART region this panel will occupy on the composed page, so the image model
    // can be asked to frame the art at that ratio and fill it (with the dialogue/caption bands reserved).
    // Every cell in a page's grid is the same size, so the cell depends only on how many panels share the
    // page (see PageLayoutCalculator); the art region within it depends on this panel's own text bands
    // (see PanelSlotLayout). Uses the design page geometry the composer lays out against.
    private static async Task<double?> PanelSlotAspectRatio(
        StoryboarderDbContext db, Panel panel, CancellationToken cancellationToken)
    {
        var panelCount = await db.Panels.CountAsync(p => p.PageId == panel.PageId, cancellationToken);
        if (panelCount <= 0)
            return null;

        var calculator = new PageLayoutCalculator(
            new PageMargins(DesignTokens.Comic.TopMargin, DesignTokens.Comic.SideMargin, DesignTokens.Comic.BottomMargin),
            DesignTokens.Comic.Gutter);
        var cell = calculator.Layout(
            new PageSize(DesignTokens.Comic.PageWidth, DesignTokens.Comic.PageHeight), panelCount)[0];
        var art = PanelSlotLayout.Compute(cell, panel.Dialogue, panel.Caption).Art;
        return art.Width / art.Height;
    }

    // Rebuilds the exact text prompt a panel was rendered with, so a content block can be logged and
    // reported with the art direction that tripped it. Uses the same builder the Gemini client does, and
    // infers whether reference sheets were attached from the panel's stored reference names.
    private static string ReconstructPanelPrompt(Novel novel, Panel panel)
    {
        var references = JsonSerializer.Deserialize<List<string>>(panel.ReferenceNamesJson ?? "[]") ?? [];
        return PanelImagePrompt.Build(novel.ArtStyle, panel.VisualPrompt, references.Count > 0);
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
        // expected resume time on the novel; a plain error retry doesn't (it's not a quota wait).
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
            new(r.Name, r.CanonicalDescription, r.Scale, JsonSerializer.Deserialize<List<string>>(r.AliasesJson) ?? []);
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

    private async Task<IReadOnlyList<byte[]>> LoadReferenceImages(
        StoryboarderDbContext db, Guid novelId, string referenceNamesJson, CancellationToken cancellationToken)
    {
        var names = JsonSerializer.Deserialize<List<string>>(referenceNamesJson) ?? [];
        if (names.Count == 0)
            return [];

        var paths = await db.DesignRefs
            .Where(r => r.NovelId == novelId && r.ReferenceImagePath != null && names.Contains(r.Name))
            .Select(r => r.ReferenceImagePath!)
            .ToListAsync(cancellationToken);

        return paths.Where(imageStore.Exists).Select(imageStore.Read).ToList();
    }

    // Splits a page's panels into consecutive groups of at most maxPerPage, balanced so the groups
    // differ in size by at most one (8 panels at a cap of 6 become 4+4, not 6+2). Each group becomes a
    // physical page. An empty page yields no groups.
    private static IEnumerable<IReadOnlyList<PanelDraft>> SplitForPages(IReadOnlyList<PanelDraft> panels, int maxPerPage)
    {
        if (panels.Count == 0)
            yield break;

        var cap = Math.Max(1, maxPerPage);
        var pageCount = (int)Math.Ceiling(panels.Count / (double)cap);
        var baseSize = panels.Count / pageCount;
        var remainder = panels.Count % pageCount; // the first `remainder` pages carry one extra panel

        var index = 0;
        for (var page = 0; page < pageCount; page++)
        {
            var size = baseSize + (page < remainder ? 1 : 0);
            yield return panels.Skip(index).Take(size).ToList();
            index += size;
        }
    }

    private static DesignRef NewDesignRef(Guid novelId, ReferenceKind kind, EntityProfile profile) => new()
    {
        NovelId = novelId,
        Kind = kind,
        Name = profile.Name,
        CanonicalDescription = profile.CanonicalDescription,
        Scale = profile.Scale,
        AliasesJson = JsonSerializer.Serialize(profile.Aliases)
    };

    private ProcessResult Fail(StoryboarderDbContext db, Novel novel, string message, CancellationToken cancellationToken)
    {
        novel.Stage = ProjectStage.Failed;
        novel.LastError = message;
        Touch(novel);
        db.SaveChanges();
        logger.LogError("Novel {NovelId} failed: {Message}", novel.Id, message);
        return ProcessResult.Idle;
    }

    // Reverts any Completed panel whose rendered image is gone from disk back to Pending (and the
    // pages that used it back to Pending too), then steps the novel back to RenderingImages so the
    // worker regenerates the missing art rather than throwing on the missing file. Returns whether
    // anything was healed.
    private async Task<bool> RevertPanelsWithMissingImages(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var volumeIds = await db.Volumes.Where(v => v.NovelId == novel.Id).Select(v => v.Id).ToListAsync(cancellationToken);
        var pageIds = await db.Pages.Where(p => volumeIds.Contains(p.VolumeId)).Select(p => p.Id).ToListAsync(cancellationToken);
        // Skipped panels are Completed with no image on purpose, so exclude them — otherwise the
        // missing-image self-heal would keep reviving them straight back into the block loop.
        var completedPanels = await db.Panels
            .Where(p => pageIds.Contains(p.PageId) && p.Status == WorkStatus.Completed && !p.Skipped)
            .ToListAsync(cancellationToken);

        var missing = completedPanels.Where(p => p.ImagePath is null || !imageStore.Exists(p.ImagePath)).ToList();
        if (missing.Count == 0)
            return false;

        var affectedPageIds = missing.Select(p => p.PageId).ToHashSet();
        foreach (var panel in missing)
        {
            panel.Status = WorkStatus.Pending;
            panel.ImagePath = null;
        }

        var affectedPages = await db.Pages.Where(p => affectedPageIds.Contains(p.Id)).ToListAsync(cancellationToken);
        foreach (var page in affectedPages)
        {
            page.CompositionStatus = WorkStatus.Pending;
            page.ComposedImagePath = null;
            page.ThumbImagePath = null;
        }

        SetStage(novel, ProjectStage.RenderingImages);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Reverts any Completed page whose composed image is gone from disk back to Pending (and the
    // volumes that used it back to Pending too), then steps the novel back to ComposingPages.
    private async Task<bool> RevertPagesWithMissingImages(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        var volumeIds = await db.Volumes.Where(v => v.NovelId == novel.Id).Select(v => v.Id).ToListAsync(cancellationToken);
        var completedPages = await db.Pages
            .Where(p => volumeIds.Contains(p.VolumeId) && p.CompositionStatus == WorkStatus.Completed)
            .ToListAsync(cancellationToken);

        var missing = completedPages.Where(p => p.ComposedImagePath is null || !imageStore.Exists(p.ComposedImagePath)).ToList();
        if (missing.Count == 0)
            return false;

        var affectedVolumeIds = missing.Select(p => p.VolumeId).ToHashSet();
        foreach (var page in missing)
        {
            page.CompositionStatus = WorkStatus.Pending;
            page.ComposedImagePath = null;
            page.ThumbImagePath = null;
        }

        var affectedVolumes = await db.Volumes.Where(v => affectedVolumeIds.Contains(v.Id)).ToListAsync(cancellationToken);
        foreach (var volume in affectedVolumes)
        {
            volume.PdfStatus = WorkStatus.Pending;
            volume.PdfPath = null;
            // Rebuild from the first page: the partial PDF (and its cursor) referenced a page that has
            // since been re-composed, so resuming it would splice in a stale/duplicate page.
            volume.PdfPageCursor = 0;
        }

        SetStage(novel, ProjectStage.ComposingPages);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void SetStage(Novel novel, ProjectStage stage)
    {
        if (novel.Stage != stage)
            logger.LogInformation("Novel {NovelId} ({Title}): {From} -> {To}", novel.Id, novel.Title, novel.Stage, stage);
        novel.Stage = stage;
        Touch(novel);
    }

    private void Touch(Novel novel) => novel.UpdatedAtUtc = clock.UtcNow;
}
