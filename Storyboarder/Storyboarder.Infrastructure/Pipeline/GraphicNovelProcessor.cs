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
    private readonly IClock clock;
    private readonly ILogger<GraphicNovelProcessor> logger;

    private readonly RequestRateLimiter claudeLimiter;
    private readonly RequestRateLimiter geminiLimiter;
    private readonly BackoffScheduler backoff;
    private readonly RateLimitOptions rateLimits;
    private readonly TextChunker chunker = new();
    private readonly DesignBibleMerger merger = new();

    public GraphicNovelProcessor(
        IDbContextFactory<StoryboarderDbContext> dbContextFactory,
        IClaudeAnalysisClient claude,
        IGeminiImageClient gemini,
        IImageStore imageStore,
        IPageComposer pageComposer,
        IVolumePdfComposer pdfComposer,
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
            .Where(n => n.Stage != ProjectStage.Completed && n.Stage != ProjectStage.Failed)
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
            }
            catch (Exception ex) when (ex is RateLimitException or HttpRequestException)
            {
                ScheduleRetry(reference, ex);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (await PendingWork(db.DesignRefs.Where(r => r.NovelId == novel.Id), r => r.ReferenceSheetStatus, r => r.NextAttemptAtUtc, now)
                is { } refWait)
            return refWait == TimeSpan.MinValue ? ProcessResult.Idle : ProcessResult.Waiting(refWait, novel.Id);

        // 4) Everything ready: advance.
        novel.Stage = ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, AllChunksAnalyzed: true, BibleConsolidated: true, AllReferenceSheetsRendered: true,
            false, false, false, false));
        Touch(novel);
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
            novel.Stage = ProjectStateMachine.NextStage(new PipelineProgress(
                novel.Stage, false, false, false, false, StoryboardCreated: true, false, false, false));
            Touch(novel);
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
            }
            catch (Exception ex) when (ex is RateLimitException or HttpRequestException)
            {
                ScheduleRetry(panel, ex);
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProcessResult.DidWork(novel.Id);
        }

        if (await PendingWork(panelsForNovel, p => p.Status, p => p.NextAttemptAtUtc, now) is { } wait)
            return wait == TimeSpan.MinValue ? ProcessResult.Idle : ProcessResult.Waiting(wait, novel.Id);

        novel.Stage = ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, false, false, false, false, AllPanelsRendered: true, false, false));
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task<ProcessResult> ComposePages(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
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
                var composed = pageComposer.Compose(panelArt);
                page.ComposedImagePath = imageStore.Save($"{novel.Id}/pages/{page.Id}.png", composed);
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

        novel.Stage = ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, false, false, false, false, false, AllPagesComposed: true, false));
        Touch(novel);
        await db.SaveChangesAsync(cancellationToken);
        return ProcessResult.DidWork(novel.Id);
    }

    private async Task<ProcessResult> AssembleVolumes(StoryboarderDbContext db, Novel novel, CancellationToken cancellationToken)
    {
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

        novel.Stage = ProjectStateMachine.NextStage(new PipelineProgress(
            novel.Stage, false, false, false, false, false, false, false, AllVolumesAssembled: true));
        Touch(novel);
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
            return;
        }

        unit.WorkState = WorkStatus.NeedsRetry;
        unit.NextAttemptAtUtc = clock.UtcNow + backoff.NextDelay(unit.Attempts - 1, retryAfter);
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

    private void SetStage(Novel novel, ProjectStage stage)
    {
        novel.Stage = stage;
        Touch(novel);
    }

    private void Touch(Novel novel) => novel.UpdatedAtUtc = clock.UtcNow;
}
