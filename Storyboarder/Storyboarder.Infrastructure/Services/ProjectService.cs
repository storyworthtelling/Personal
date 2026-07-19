using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
using Storyboarder.Domain.Time;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.Persistence;

namespace Storyboarder.Infrastructure.Services;

// App-level state the diagnostics report needs but that lives in the platform head (MAUI Preferences),
// passed in so the service stays platform-free.
public sealed record DiagnosticsContext(
    bool ClaudeKeySet, bool GeminiKeySet, string GoogleAccount,
    string LastRunOutcome, DateTimeOffset? LastRunAtUtc, string? MigrationError);

// Application service backing the controllers: creates a novel from a picked Google Doc and reports
// progress. Controllers go through this rather than touching the DbContext directly.
public sealed class ProjectService
{
    private readonly IDbContextFactory<StoryboarderDbContext> dbContextFactory;
    private readonly IGoogleDocsClient docs;
    private readonly IImageStore imageStore;
    private readonly IClock clock;
    private readonly RateLimitOptions rateLimits;

    public ProjectService(
        IDbContextFactory<StoryboarderDbContext> dbContextFactory, IGoogleDocsClient docs,
        IImageStore imageStore, IClock clock, IOptions<RateLimitOptions> rateLimitOptions)
    {
        this.dbContextFactory = dbContextFactory;
        this.docs = docs;
        this.imageStore = imageStore;
        this.clock = clock;
        rateLimits = rateLimitOptions.Value;
    }

    public async Task<Guid> CreateFromDocAsync(
        string fileId, string fileName, string accessToken, string? title, string? artStyle, CancellationToken cancellationToken)
    {
        var text = await docs.FetchText(fileId, accessToken, cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.UtcNow;
        var novel = new Novel
        {
            Title = string.IsNullOrWhiteSpace(title) ? fileName : title,
            GoogleDocId = fileId,
            GoogleDocName = fileName,
            ArtStyle = string.IsNullOrWhiteSpace(artStyle) ? ArtStyles.Default : artStyle,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = text, CharacterCount = text.Length };

        db.Novels.Add(novel);
        await db.SaveChangesAsync(cancellationToken);
        return novel.Id;
    }

    // True while any novel is mid-conversion (not Completed/Failed/Cancelled). The Android head checks
    // this on launch to kick the worker immediately after an app update, so a long conversion resumes
    // at once rather than waiting for the next periodic window.
    public async Task<bool> HasActiveWorkAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Novels.AnyAsync(
            n => n.Stage != ProjectStage.Completed && n.Stage != ProjectStage.Failed &&
                 n.Stage != ProjectStage.Cancelled, cancellationToken);
    }

    // Stops a running conversion without removing it: the worker skips Cancelled novels, but the card
    // and any finished PDFs remain in the library. A no-op if the novel is already in a terminal stage.
    public async Task CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null ||
            novel.Stage is ProjectStage.Completed or ProjectStage.Failed or ProjectStage.Cancelled)
            return;

        novel.Stage = ProjectStage.Cancelled;
        novel.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // Un-sticks a stalled project on demand (the "Retry" action): clears the novel-level error, gives
    // the current storyboard window a fresh attempt budget, and makes every failed/retrying unit due
    // immediately so the next worker cycle resumes the work at once. The UI pairs this with a RunNow so
    // it doesn't wait for the next periodic window. A no-op on a finished or user-cancelled project.
    public async Task RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null || novel.Stage is ProjectStage.Completed or ProjectStage.Cancelled)
            return;

        novel.LastError = null;
        novel.StoryboardAttempts = 0;
        novel.UpdatedAtUtc = clock.UtcNow;

        Revive(await db.Chunks.Where(c => c.NovelId == id).ToListAsync(cancellationToken));
        Revive(await db.DesignRefs.Where(d => d.NovelId == id).ToListAsync(cancellationToken));
        Revive(await db.ChunkAnalyses.Where(a => a.NovelId == id).ToListAsync(cancellationToken));

        var volumeIds = await db.Volumes.Where(v => v.NovelId == id).Select(v => v.Id).ToListAsync(cancellationToken);
        var pages = await db.Pages.Where(p => volumeIds.Contains(p.VolumeId)).ToListAsync(cancellationToken);
        var pageIds = pages.Select(p => p.Id).ToList();
        Revive(await db.Panels.Where(p => pageIds.Contains(p.PageId)).ToListAsync(cancellationToken));

        // Pages compose locally (no backoff / IRetryable), so revive a failed composition directly.
        foreach (var page in pages.Where(p => p.CompositionStatus is WorkStatus.Failed or WorkStatus.NeedsRetry))
        {
            page.CompositionStatus = WorkStatus.Pending;
            page.Attempts = 0;
            page.LastError = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // The panels parked awaiting the user's OK after a safety block, with the flagged categories and the
    // beat's dialogue/caption so the review screen can show what's being decided.
    public async Task<IReadOnlyList<BlockedPanel>> GetBlockedPanelsAsync(Guid novelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var volumeIds = await db.Volumes.Where(v => v.NovelId == novelId).Select(v => v.Id).ToListAsync(cancellationToken);
        var pageIds = await db.Pages.Where(p => volumeIds.Contains(p.VolumeId)).Select(p => p.Id).ToListAsync(cancellationToken);

        var panels = await db.Panels
            .Where(p => pageIds.Contains(p.PageId) && p.Status == WorkStatus.AwaitingApproval)
            .OrderBy(p => p.PanelNumber)
            .ToListAsync(cancellationToken);

        return panels
            .Select(p => new BlockedPanel(
                p.Id, p.PanelNumber, p.VisualPrompt, p.Dialogue, p.Caption,
                JsonSerializer.Deserialize<List<string>>(p.BlockedCategoriesJson ?? "[]") ?? [],
                p.LastError))
            .ToList();
    }

    // The user approved rendering a safety-blocked panel: relax the flagged category for its next render.
    // The panel goes back to Pending (due now) with the approval flag set; the worker's next cycle
    // re-renders it with only that category relaxed. Returns false if the panel isn't awaiting approval.
    public async Task<bool> ApprovePanelRenderAsync(Guid panelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var panel = await db.Panels.FirstOrDefaultAsync(p => p.Id == panelId, cancellationToken);
        if (panel is null || panel.Status != WorkStatus.AwaitingApproval)
            return false;

        panel.Status = WorkStatus.Pending;
        panel.ApprovedForRelaxedRender = true;
        panel.Attempts = 0;
        panel.NextAttemptAtUtc = null;
        panel.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // The user chose to leave a safety-blocked panel out: mark it done-but-skipped so stage advancement
    // proceeds and page composition draws the page without it. Returns false if it isn't awaiting approval.
    public async Task<bool> SkipPanelRenderAsync(Guid panelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var panel = await db.Panels.FirstOrDefaultAsync(p => p.Id == panelId, cancellationToken);
        if (panel is null || panel.Status != WorkStatus.AwaitingApproval)
            return false;

        panel.Status = WorkStatus.Completed;
        panel.Skipped = true;
        panel.ApprovedForRelaxedRender = false;
        panel.ImagePath = null;
        panel.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // The prohibited/IP-blocked panel a Cancelled novel is stuck on, with its current prompt and the
    // block reason, so the rework screen can prefill the art direction the user needs to revise. Null
    // unless the novel is genuinely Cancelled with an offending panel to rework.
    public async Task<ProhibitedPanel?> GetProhibitedPanelAsync(Guid novelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == novelId, cancellationToken);
        if (novel is null || novel.Stage != ProjectStage.Cancelled)
            return null;

        var panel = await FindProhibitedPanelAsync(db, novelId, cancellationToken);
        return panel is null ? null : new ProhibitedPanel(panel.Id, panel.PanelNumber, panel.VisualPrompt, panel.LastError);
    }

    // The user reworked a prohibited-blocked panel's art direction to avoid the gated content: swap in
    // the new visual prompt, reset the panel for a clean render, clear the fallout, and lift the novel
    // out of Cancelled back into RenderingImages so the next worker cycle re-renders that one panel and
    // the book carries on — without discarding the panels already rendered. Returns the reworked panel's
    // number, or null if the novel isn't Cancelled with an offending panel to rework. The UI pairs this
    // with a RunNow so the decision takes effect at once.
    public async Task<int?> ReworkBlockedPanelAndResumeAsync(
        Guid novelId, string newVisualPrompt, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == novelId, cancellationToken);
        if (novel is null || novel.Stage != ProjectStage.Cancelled)
            return null;

        var panel = await FindProhibitedPanelAsync(db, novelId, cancellationToken);
        if (panel is null)
            return null;

        panel.VisualPrompt = newVisualPrompt;
        panel.Status = WorkStatus.Pending;
        panel.Attempts = 0;
        panel.NextAttemptAtUtc = null;
        panel.LastError = null;
        panel.BlockedCategoriesJson = null;
        panel.ApprovedForRelaxedRender = false;
        panel.PromptRewrittenForSafety = false;
        panel.Skipped = false;

        novel.LastError = null;
        novel.Stage = ProjectStage.RenderingImages;
        novel.UpdatedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return panel.PanelNumber;
    }

    // The panel a Prohibited/IP block stopped the book on, carrying the provider's block message on
    // LastError. The render loop settles that panel into Pending as it cancels, so a Pending panel with an
    // error is the primary match (lowest number wins, though there's normally just one). A NeedsRetry
    // panel is the fallback: a block that landed mid-retry — or an older cancel from before the loop
    // settled it to Pending — can leave the offending panel there, and it must still be reworkable rather
    // than the Rework button silently doing nothing. Pending is preferred so a fresh cancel picks the real
    // offender, not a lower-numbered panel merely parked mid-retry on a transient error.
    private static async Task<Panel?> FindProhibitedPanelAsync(
        StoryboarderDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var volumeIds = await db.Volumes.Where(v => v.NovelId == novelId).Select(v => v.Id).ToListAsync(cancellationToken);
        var pageIds = await db.Pages.Where(p => volumeIds.Contains(p.VolumeId)).Select(p => p.Id).ToListAsync(cancellationToken);
        var blocked = db.Panels.Where(p => pageIds.Contains(p.PageId) && p.LastError != null);

        var pending = await blocked
            .Where(p => p.Status == WorkStatus.Pending)
            .OrderBy(p => p.PanelNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return pending ?? await blocked
            .Where(p => p.Status == WorkStatus.NeedsRetry)
            .OrderBy(p => p.PanelNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Returns a failed/retrying unit to Pending, due now, with a fresh attempt budget and no error.
    private static void Revive(IEnumerable<IRetryable> units)
    {
        foreach (var unit in units.Where(u => u.WorkState is WorkStatus.Failed or WorkStatus.NeedsRetry))
        {
            unit.WorkState = WorkStatus.Pending;
            unit.Attempts = 0;
            unit.NextAttemptAtUtc = null;
            unit.LastError = null;
        }
    }

    // Removes a novel entirely: its rows (children cascade via the DbContext relationships) and every
    // rendered image/PDF on disk. Used to clear a project out of the library for good.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null)
            return;

        db.Novels.Remove(novel);
        await db.SaveChangesAsync(cancellationToken);

        // The DB row is gone; drop the output subtree too. Files failing to delete shouldn't resurrect
        // the (already-removed) project, so this runs after the row is committed.
        imageStore.Delete(id.ToString());
    }

    public async Task<IReadOnlyList<NovelStatus>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Novels.OrderByDescending(n => n.CreatedAtUtc).Select(n => n.Id).ToListAsync(cancellationToken);
        var statuses = new List<NovelStatus>(ids.Count);
        var now = clock.UtcNow;
        foreach (var id in ids)
        {
            var status = await BuildStatus(db, id, rateLimits.MaxAttempts, now, cancellationToken);
            if (status is not null)
                statuses.Add(status);
        }
        return statuses;
    }

    public async Task<NovelStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await BuildStatus(db, id, rateLimits.MaxAttempts, clock.UtcNow, cancellationToken);
    }

    public async Task<string?> GetVolumePdfPathAsync(Guid novelId, int number, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Volumes
            .Where(v => v.NovelId == novelId && v.Number == number && v.PdfStatus == WorkStatus.Completed)
            .Select(v => v.PdfPath)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetAnalysisPdfPathAsync(Guid novelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BookAnalyses
            .Where(a => a.NovelId == novelId && a.PdfStatus == WorkStatus.Completed)
            .Select(a => a.PdfPath)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<NovelStatus?> BuildStatus(
        StoryboarderDbContext db, Guid id, int maxStoryboardAttempts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null)
            return null;

        var chunks = await Counts(db.Chunks.Where(c => c.NovelId == id), c => c.AnalysisStatus, cancellationToken);
        var sheets = await Counts(db.DesignRefs.Where(r => r.NovelId == id), r => r.ReferenceSheetStatus, cancellationToken);

        // Storyboarding walks the chunk list one window at a time; the cursor is how far it has got.
        // Clamp to the total so the moment it finishes (cursor == count) it reads a clean 100%.
        var chunkCount = await db.Chunks.CountAsync(c => c.NovelId == id, cancellationToken);
        var storyboard = new PhaseProgress(Math.Min(novel.StoryboardCursor, chunkCount), chunkCount);

        var volumeIds = await db.Volumes.Where(v => v.NovelId == id).Select(v => v.Id).ToListAsync(cancellationToken);
        var pageIds = await db.Pages.Where(p => volumeIds.Contains(p.VolumeId)).Select(p => p.Id).ToListAsync(cancellationToken);

        var panels = await Counts(db.Panels.Where(p => pageIds.Contains(p.PageId)), p => p.Status, cancellationToken);
        var pages = await Counts(db.Pages.Where(p => volumeIds.Contains(p.VolumeId)), p => p.CompositionStatus, cancellationToken);
        var volumes = await Counts(db.Volumes.Where(v => v.NovelId == id), v => v.PdfStatus, cancellationToken);

        var volumeList = await db.Volumes
            .Where(v => v.NovelId == id)
            .OrderBy(v => v.Number)
            .Select(v => new VolumeStatus(v.Number, v.Title, v.PdfStatus == WorkStatus.Completed))
            .ToListAsync(cancellationToken);

        // The analysis stage maps every chapter into notes before it consolidates and renders; the
        // per-chapter map is the long part, so surface it as its own progress.
        var analysisNotes = await Counts(db.ChunkAnalyses.Where(a => a.NovelId == id), a => a.Status, cancellationToken);
        var analysisReady = await db.BookAnalyses
            .AnyAsync(a => a.NovelId == id && a.PdfStatus == WorkStatus.Completed, cancellationToken);
        var analysis = new PhaseProgress(analysisReady ? 1 : 0, 1);

        // The first composed page doubles as the library cover thumbnail.
        var coverThumb = await db.Pages
            .Where(p => volumeIds.Contains(p.VolumeId) && p.CompositionStatus == WorkStatus.Completed)
            .OrderBy(p => p.PageNumber)
            .Select(p => p.ThumbImagePath ?? p.ComposedImagePath)
            .FirstOrDefaultAsync(cancellationToken);

        // Panels safety-blocked mid-render and parked until the user approves relaxing the flagged
        // category (or skips them). The book can't finalize while any remain.
        var pendingApprovals = await db.Panels
            .CountAsync(p => pageIds.Contains(p.PageId) && p.Status == WorkStatus.AwaitingApproval, cancellationToken);

        // The pipeline auto-retries transient failures (network drops, rate limits) with backoff, so a
        // novel-level LastError is usually a self-healing hiccup, not a stall. Two things genuinely need
        // the user: a storyboard window that has burned through its bounded retry budget, and any panel
        // parked awaiting the user's OK. Everything else stays quiet while the worker self-heals.
        var needsAttention = pendingApprovals > 0
            || (novel.LastError is not null
                && novel.Stage == ProjectStage.Storyboarding
                && novel.StoryboardCursor < chunkCount
                && novel.StoryboardAttempts >= maxStoryboardAttempts);

        // Only surface the resume time while it's still in the future; once it passes it's stale, so the
        // card stops showing it (and the worker has presumably resumed).
        var rateLimitedUntil = novel.RateLimitedUntilUtc is { } until && until > now ? novel.RateLimitedUntilUtc : null;

        return new NovelStatus(
            novel.Id, novel.Title, novel.Stage.ToString(), novel.LastError,
            chunks, sheets, storyboard, panels, pages, volumes, volumeList,
            analysisNotes, analysis, analysisReady, coverThumb, needsAttention, pendingApprovals, rateLimitedUntil);
    }

    // A human-readable snapshot of everything that might be wrong: config, last worker run, and per
    // project the stage plus each phase's done/failed/retry counts and a sample of the error messages
    // on the stuck units. Surfaces "sheets 0 of 124, 124 failed: <reason>" without reading the raw log.
    public async Task<string> BuildDiagnosticsReportAsync(DiagnosticsContext context, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var report = new StringBuilder();

        report.AppendLine($"Storyboarder diagnostics — {clock.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        report.AppendLine($"Claude key: {(context.ClaudeKeySet ? "set" : "MISSING")}    Gemini key: {(context.GeminiKeySet ? "set" : "MISSING")}");
        report.AppendLine($"Google account: {(string.IsNullOrEmpty(context.GoogleAccount) ? "not signed in" : context.GoogleAccount)}");
        report.Append("Last worker run: ");
        report.AppendLine(string.IsNullOrEmpty(context.LastRunOutcome)
            ? "never"
            : context.LastRunOutcome + (context.LastRunAtUtc is { } at ? $" at {at:yyyy-MM-dd HH:mm:ss}Z" : string.Empty));
        if (!string.IsNullOrEmpty(context.MigrationError))
            report.AppendLine($"Migration error: {context.MigrationError}");

        var novels = await db.Novels.OrderByDescending(n => n.CreatedAtUtc).ToListAsync(cancellationToken);
        report.AppendLine();
        report.AppendLine($"Projects: {novels.Count}");

        foreach (var novel in novels)
        {
            report.AppendLine();
            report.AppendLine($"— {novel.Title} [{novel.Id}]");
            report.AppendLine($"  Stage: {novel.Stage}    Updated: {novel.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss}Z    StoryboardCursor: {novel.StoryboardCursor}");
            if (!string.IsNullOrEmpty(novel.LastError))
                report.AppendLine($"  Novel error: {Truncate(novel.LastError)}");

            await AppendPhase(report, "Chunks", db.Chunks.Where(c => c.NovelId == novel.Id),
                c => c.AnalysisStatus, c => c.LastError, cancellationToken);
            await AppendPhase(report, "Reference sheets", db.DesignRefs.Where(r => r.NovelId == novel.Id),
                r => r.ReferenceSheetStatus, r => r.LastError, cancellationToken);

            var volumeIds = await db.Volumes.Where(v => v.NovelId == novel.Id).Select(v => v.Id).ToListAsync(cancellationToken);
            var pageIds = await db.Pages.Where(p => volumeIds.Contains(p.VolumeId)).Select(p => p.Id).ToListAsync(cancellationToken);
            await AppendPhase(report, "Panels", db.Panels.Where(p => pageIds.Contains(p.PageId)),
                p => p.Status, p => p.LastError, cancellationToken);
            await AppendPhase(report, "Pages", db.Pages.Where(p => volumeIds.Contains(p.VolumeId)),
                p => p.CompositionStatus, p => p.LastError, cancellationToken);
            // Volumes carry no LastError column, so there's no message to sample (PdfPath is null on a
            // failed volume); the failed/retry counts on the line are what matter here.
            await AppendPhase(report, "Volumes", db.Volumes.Where(v => v.NovelId == novel.Id),
                v => v.PdfStatus, v => v.PdfPath, cancellationToken);
            await AppendPhase(report, "Analysis notes", db.ChunkAnalyses.Where(a => a.NovelId == novel.Id),
                a => a.Status, a => a.LastError, cancellationToken);
        }

        return report.ToString();
    }

    // A focused capture of every panel a content filter stopped — the actionable detail the top-level
    // status snapshot leaves out: the exact prompt sent to the image model (reconstructed identically to
    // the render call), the block reason and flagged categories, the reference sheets in play, and the
    // beat's dialogue/caption. This is what a prohibited/IP block needs before its art direction can be
    // reworked and the book resumed. Covers panels parked for approval (safety) and panels stopped
    // Pending with a block message (prohibited) — not the transient failures the worker auto-retries.
    public async Task<string> BuildBlockedPanelReportAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var report = new StringBuilder();
        report.AppendLine($"Storyboarder blocked-panel report — {clock.UtcNow:yyyy-MM-dd HH:mm:ss}Z");

        var novels = await db.Novels.OrderByDescending(n => n.CreatedAtUtc).ToListAsync(cancellationToken);
        var totalBlocked = 0;

        foreach (var novel in novels)
        {
            var volumeIds = await db.Volumes.Where(v => v.NovelId == novel.Id).Select(v => v.Id).ToListAsync(cancellationToken);
            var pageIds = await db.Pages.Where(p => volumeIds.Contains(p.VolumeId)).Select(p => p.Id).ToListAsync(cancellationToken);
            var panels = await db.Panels
                .Where(p => pageIds.Contains(p.PageId) &&
                            (p.Status == WorkStatus.AwaitingApproval ||
                             (p.Status == WorkStatus.Pending && p.LastError != null)))
                .OrderBy(p => p.PanelNumber)
                .ToListAsync(cancellationToken);
            if (panels.Count == 0)
                continue;

            totalBlocked += panels.Count;
            report.AppendLine();
            report.AppendLine($"— {novel.Title} [{novel.Id}]");
            report.AppendLine($"  Stage: {novel.Stage}    Art style: {novel.ArtStyle}");
            if (!string.IsNullOrEmpty(novel.LastError))
                report.AppendLine($"  Novel error: {novel.LastError}");

            foreach (var panel in panels)
            {
                var references = JsonSerializer.Deserialize<List<string>>(panel.ReferenceNamesJson ?? "[]") ?? [];
                var categories = JsonSerializer.Deserialize<List<string>>(panel.BlockedCategoriesJson ?? "[]") ?? [];
                report.AppendLine();
                report.AppendLine($"  Panel {panel.PanelNumber} [{panel.Id}] — {panel.Status}");
                if (categories.Count > 0)
                    report.AppendLine($"    Flagged categories: {string.Join(", ", categories)}");
                if (!string.IsNullOrEmpty(panel.LastError))
                    report.AppendLine($"    Provider reason: {panel.LastError}");
                report.AppendLine($"    References: {(references.Count > 0 ? string.Join(", ", references) : "(none)")}");
                if (!string.IsNullOrEmpty(panel.Dialogue))
                    report.AppendLine($"    Dialogue: {panel.Dialogue}");
                if (!string.IsNullOrEmpty(panel.Caption))
                    report.AppendLine($"    Caption: {panel.Caption}");
                report.AppendLine("    Prompt sent to Gemini:");
                report.AppendLine($"      {PanelImagePrompt.Build(novel.ArtStyle, panel.VisualPrompt, references.Count > 0)}");
            }
        }

        if (totalBlocked == 0)
            report.AppendLine().AppendLine("No blocked panels found.");

        return report.ToString();
    }

    // Writes one phase line ("done/total, N failed, M retrying") plus a short sample of the distinct
    // error messages on its failed/retrying units. Skips phases the pipeline hasn't reached yet.
    private static async Task AppendPhase<T>(
        StringBuilder report, string name, IQueryable<T> query,
        System.Linq.Expressions.Expression<Func<T, WorkStatus>> status,
        System.Linq.Expressions.Expression<Func<T, string?>> lastError,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return;

        var done = await query.CountAsync(BuildEquals(status, WorkStatus.Completed), cancellationToken);
        var failed = await query.CountAsync(BuildEquals(status, WorkStatus.Failed), cancellationToken);
        var retry = await query.CountAsync(BuildEquals(status, WorkStatus.NeedsRetry), cancellationToken);

        var line = $"  {name}: {done}/{total} done";
        if (failed > 0) line += $", {failed} failed";
        if (retry > 0) line += $", {retry} retrying";
        report.AppendLine(line);

        if (failed == 0 && retry == 0)
            return;

        var messages = await query.Where(BuildTroubling(status)).Select(lastError).ToListAsync(cancellationToken);
        foreach (var group in messages
                     .Where(m => !string.IsNullOrEmpty(m))
                     .GroupBy(m => m!)
                     .OrderByDescending(g => g.Count())
                     .Take(3))
            report.AppendLine($"      • {(group.Count() > 1 ? $"(×{group.Count()}) " : string.Empty)}{Truncate(group.Key)}");
    }

    private static string Truncate(string value, int max = 300) =>
        value.Length <= max ? value : value[..max] + "…";

    // status == Failed || status == NeedsRetry, for sampling the errors on stuck units.
    private static System.Linq.Expressions.Expression<Func<T, bool>> BuildTroubling<T>(
        System.Linq.Expressions.Expression<Func<T, WorkStatus>> selector)
    {
        var failed = System.Linq.Expressions.Expression.Equal(
            selector.Body, System.Linq.Expressions.Expression.Constant(WorkStatus.Failed));
        var retry = System.Linq.Expressions.Expression.Equal(
            selector.Body, System.Linq.Expressions.Expression.Constant(WorkStatus.NeedsRetry));
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
            System.Linq.Expressions.Expression.OrElse(failed, retry), selector.Parameters);
    }

    private static async Task<PhaseProgress> Counts<T>(
        IQueryable<T> query, System.Linq.Expressions.Expression<Func<T, WorkStatus>> status, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var done = await query.CountAsync(BuildEquals(status, WorkStatus.Completed), cancellationToken);
        return new PhaseProgress(done, total);
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> BuildEquals<T>(
        System.Linq.Expressions.Expression<Func<T, WorkStatus>> selector, WorkStatus value)
    {
        var body = System.Linq.Expressions.Expression.Equal(
            selector.Body, System.Linq.Expressions.Expression.Constant(value));
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }
}
