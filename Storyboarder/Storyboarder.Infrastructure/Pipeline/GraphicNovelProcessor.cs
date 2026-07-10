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
    private readonly TextChunker chunker = new();
    private readonly DesignBibleMerger merger = new();
    private readonly CoverPageRenderer coverRenderer = new();

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
        claudeLimiter = new RequestRateLimiter(rateLimits.ClaudeRequestsPerMinute, rateLimits.ClaudeRequestsPerDay, clock);
        geminiLimiter = new RequestRateLimiter(rateLimits.GeminiRequestsPerMinute, rateLimits.GeminiRequestsPerDay, clock);
        backoff = new BackoffScheduler(
            TimeSpan.FromSeconds(rateLimits.BaseDelaySeconds), TimeSpan.FromSeconds(rateLimits.MaxDelaySeconds));
    }

    public async Task<ProcessResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var novel = await db.Novels
            .Where(n => n.Stage != ProjectStage.Completed && n.Stage != ProjectStage.Failed &&
                        n.Stage != ProjectStage.Cancelled)
            .OrderBy(n => n.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (novel is null)
            return ProcessResult.Idle;

        return novel.Stage switch
        {
            ProjectStage.Created => await Advance(db, novel, cancellationToken),
            ProjectStage.Ingesting => await Ingest(db, novel, cancellationToken),
            ProjectStage.BuildingDesignBible => await BuildDesignBible(db, novel, cancellationToken),
            ProjectStage.Storyboarding => await DoStoryboard(db, novel, cancellationToken),
            ProjectStage.RenderingImages => await RenderPanels(db, novel, cancellationToken),
            ProjectStage.ComposingPages => await ComposePages(db, novel, cancellationToken),
            ProjectStage.AssemblingVolumes => await AssembleVolumes(db, novel, cancellationToken),
            ProjectStage.ComposingAnalysis => await ComposeAnalysis(db, novel, cancellationToken),
            _ => ProcessResult.Idle
        };
    }

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
        }

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
            catch (Exception ex) when (ex is RateLimitException or HttpRequestException)
            {
                ScheduleRetry(chunk, ex);
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

        // 3) Render one pending/due reference sheet.
        var reference = await db.DesignRefs
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
                var image = await gemini.GenerateReferenceSheet(reference.CanonicalDescription, novel.ArtStyle, cancellationToken);
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
                ScheduleRetry(reference, ex);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // Reference sheets are optional consistency aids — panels render with whatever sheets exist —
        // so a permanently-failed sheet must not block the stage. Only sheets still awaiting a retry
        // hold us here; once none remain Pending/NeedsRetry, advance even if some ended up Failed.
        var pendingSheetRetries = await db.DesignRefs
            .Where(r => r.NovelId == novel.Id && r.ReferenceSheetStatus == WorkStatus.NeedsRetry)
            .Select(r => r.NextAttemptAtUtc)
            .ToListAsync(cancellationToken);
        if (pendingSheetRetries.Count > 0)
            return ProcessResult.Waiting((pendingSheetRetries.Min() ?? now) - now, novel.Id);

        // 4) Everything ready: advance.
        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, AllChunksAnalyzed: true, BibleConsolidated: true, AllReferenceSheetsRendered: true,
            false, false, false, false)));
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

        var window = await db.Chunks
            .Where(c => c.NovelId == novel.Id && c.Ordinal == novel.StoryboardCursor)
            .Select(c => c.Text)
            .FirstAsync(cancellationToken);

        if (!claudeLimiter.TryAcquire())
            return ProcessResult.Waiting(claudeLimiter.TimeUntilNext(), novel.Id);

        var bible = await LoadBible(db, novel.Id, cancellationToken);

        StoryboardSegment segment;
        try
        {
            segment = await claude.Storyboard(window, novel.ContinuitySummary, bible, novel.ArtStyle, cancellationToken);
        }
        catch (Exception ex) when (ex is RateLimitException or HttpRequestException)
        {
            novel.LastError = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            var retryAfter = (ex as RateLimitException)?.RetryAfter;
            return ProcessResult.Waiting(backoff.NextDelay(0, retryAfter), novel.Id);
        }

        var volumes = await db.Volumes
            .Where(v => v.NovelId == novel.Id)
            .Include(v => v.Pages)
            .ToListAsync(cancellationToken);

        foreach (var volumeDraft in segment.Volumes)
        {
            var volume = volumes.FirstOrDefault(v => v.Number == volumeDraft.Number);
            if (volume is null)
            {
                volume = new Volume { NovelId = novel.Id, Number = volumeDraft.Number, Title = volumeDraft.Title };
                db.Volumes.Add(volume);
                volumes.Add(volume);
            }

            foreach (var pageDraft in volumeDraft.Pages)
            {
                var page = new Page
                {
                    VolumeId = volume.Id,
                    ChapterNumber = pageDraft.ChapterNumber,
                    PageNumber = volume.Pages.Count + 1
                };
                int panelNumber = 1;
                foreach (var panelDraft in pageDraft.Panels)
                    page.Panels.Add(new Panel
                    {
                        PageId = page.Id,
                        PanelNumber = panelNumber++,
                        VisualPrompt = panelDraft.VisualPrompt,
                        Dialogue = panelDraft.Dialogue,
                        Caption = panelDraft.Caption,
                        ReferenceNamesJson = JsonSerializer.Serialize(panelDraft.ReferenceNames, JsonResponse.CaseInsensitive)
                    });
                volume.Pages.Add(page);
            }
        }

        novel.ContinuitySummary = segment.ContinuitySummary;
        novel.StoryboardCursor++;
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
                var image = await gemini.GeneratePanel(panel.VisualPrompt, novel.ArtStyle, references, cancellationToken);
                panel.ImagePath = imageStore.Save($"{novel.Id}/panels/{panel.Id}.png", image);
                panel.Status = WorkStatus.Completed;
                panel.LastError = null;
                logger.LogInformation("Rendered panel {PanelId} (novel {NovelId})", panel.Id, novel.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // As with reference sheets: a rate limit, HTTP error, or no-image response is retried
                // with backoff (then Failed once attempts run out) rather than escaping and crashing
                // the cycle. A permanently-failed panel still blocks page composition below, by design.
                ScheduleRetry(panel, ex);
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
                var panelArt = page.Panels
                    .OrderBy(p => p.PanelNumber)
                    .Select(p => new PanelArt(imageStore.Read(p.ImagePath!), p.Dialogue, p.Caption))
                    .ToList();
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

        var volume = await db.Volumes
            .Where(v => v.NovelId == novel.Id && v.PdfStatus == WorkStatus.Pending)
            .Include(v => v.Pages)
            .OrderBy(v => v.Number)
            .FirstOrDefaultAsync(cancellationToken);

        if (volume is not null)
        {
            try
            {
                var pageImages = volume.Pages
                    .OrderBy(p => p.PageNumber)
                    .Select(p => imageStore.Read(p.ComposedImagePath!))
                    .ToList();
                pageImages.Insert(0, coverRenderer.VolumeCover(novel.Title, volume.Number, volume.Title));
                var pdf = pdfComposer.Compose(pageImages);
                volume.PdfPath = imageStore.Save($"{novel.Id}/volumes/volume-{volume.Number}.pdf", pdf);
                volume.PdfStatus = WorkStatus.Completed;
            }
            catch (Exception ex)
            {
                volume.PdfStatus = WorkStatus.Failed;
                logger.LogError(ex, "Failed to assemble volume {VolumeId}", volume.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (await db.Volumes.AnyAsync(v => v.NovelId == novel.Id && v.PdfStatus != WorkStatus.Completed, cancellationToken))
            return ProcessResult.Idle; // blocked on a failed volume

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

        // 0) Seed one analysis unit per chunk (once), plus the per-novel analysis record.
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

            var text = await db.Chunks
                .Where(c => c.NovelId == novel.Id && c.Ordinal == unit.Ordinal)
                .Select(c => c.Text)
                .FirstAsync(cancellationToken);
            var castNames = await db.DesignRefs
                .Where(r => r.NovelId == novel.Id && r.Kind == ReferenceKind.Character)
                .Select(r => r.Name)
                .ToListAsync(cancellationToken);

            try
            {
                var notes = await claude.AnalyzeChapter(text, castNames, cancellationToken);
                unit.NotesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
                unit.Status = WorkStatus.Completed;
                unit.LastError = null;
            }
            catch (Exception ex) when (ex is RateLimitException or HttpRequestException)
            {
                ScheduleRetry(unit, ex);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (await PendingWork(db.ChunkAnalyses.Where(a => a.NovelId == novel.Id), a => a.Status, a => a.NextAttemptAtUtc, now)
                is { } mapWait)
            return mapWait == TimeSpan.MinValue ? ProcessResult.Idle : ProcessResult.Waiting(mapWait, novel.Id);

        var analysis = await db.BookAnalyses.FirstAsync(a => a.NovelId == novel.Id, cancellationToken);

        // 2) Reduce: consolidate all chunk notes into the whole report (one Claude call).
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
            catch (Exception ex) when (ex is RateLimitException or HttpRequestException)
            {
                novel.LastError = ex.Message;
                await db.SaveChangesAsync(cancellationToken);
                var retryAfter = (ex as RateLimitException)?.RetryAfter;
                return ProcessResult.Waiting(backoff.NextDelay(0, retryAfter), novel.Id);
            }

            Touch(novel);
            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 3) Render: compose the analysis PDF from the consolidated report.
        if (analysis.PdfStatus != WorkStatus.Completed)
        {
            try
            {
                var report = JsonSerializer.Deserialize<BookAnalysisResult>(analysis.ConsolidatedJson, JsonResponse.CaseInsensitive)
                             ?? BookAnalysisResult.Empty;
                var pdf = analysisPdfComposer.Compose(report, novel.Title);
                analysis.PdfPath = imageStore.Save($"{novel.Id}/analysis/book-analysis.pdf", pdf);
                analysis.PdfStatus = WorkStatus.Completed;
            }
            catch (Exception ex)
            {
                analysis.PdfStatus = WorkStatus.Failed;
                logger.LogError(ex, "Failed to compose analysis PDF for novel {NovelId}", novel.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        // 4) Report rendered: advance to Completed.
        SetStage(novel, ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, SourceIngested: false, AllChunksAnalyzed: false, BibleConsolidated: false,
            AllReferenceSheetsRendered: false, StoryboardCreated: false, AllPanelsRendered: false,
            AllPagesComposed: false, AllVolumesAssembled: false, AnalysisComplete: true)));
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    // --- helpers ---

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

    private void ScheduleRetry(IRetryable unit, Exception exception)
    {
        unit.Attempts++;
        unit.LastError = exception.Message;
        var retryAfter = (exception as RateLimitException)?.RetryAfter;
        var transient = exception is RateLimitException;

        if (!transient && unit.Attempts >= rateLimits.MaxAttempts)
        {
            unit.WorkState = WorkStatus.Failed;
            logger.LogWarning("{Unit} failed after {Attempts} attempts and is given up on: {Error}",
                unit.GetType().Name, unit.Attempts, exception.Message);
            return;
        }

        unit.WorkState = WorkStatus.NeedsRetry;
        unit.NextAttemptAtUtc = clock.UtcNow + backoff.NextDelay(unit.Attempts - 1, retryAfter);
        logger.LogWarning("{Unit} attempt {Attempts}/{Max} failed ({Kind}); retrying after {NextAttempt:u}: {Error}",
            unit.GetType().Name, unit.Attempts, rateLimits.MaxAttempts,
            transient ? "rate limit" : "error", unit.NextAttemptAtUtc, exception.Message);
    }

    private async Task<DesignBible> LoadBible(StoryboarderDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var refs = await db.DesignRefs.Where(r => r.NovelId == novelId).ToListAsync(cancellationToken);
        EntityProfile Map(DesignRef r) =>
            new(r.Name, r.CanonicalDescription, JsonSerializer.Deserialize<List<string>>(r.AliasesJson) ?? []);
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

    private static DesignRef NewDesignRef(Guid novelId, ReferenceKind kind, EntityProfile profile) => new()
    {
        NovelId = novelId,
        Kind = kind,
        Name = profile.Name,
        CanonicalDescription = profile.CanonicalDescription,
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
        var completedPanels = await db.Panels
            .Where(p => pageIds.Contains(p.PageId) && p.Status == WorkStatus.Completed)
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
