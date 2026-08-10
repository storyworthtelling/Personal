using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
using Storyboarder.Domain.Time;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.External;
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
        // First draft of a new project: the project is identified by this draft's own id.
        novel.ProjectId = novel.Id;
        novel.Source = new SourceText { NovelId = novel.Id, FullText = text, CharacterCount = text.Length };

        db.Novels.Add(novel);
        await db.SaveChangesAsync(cancellationToken);
        return novel.Id;
    }

    // Adds the next draft to an existing project: the owner re-picks a doc (the revised manuscript) and
    // gets a fresh conversion that shares the previous draft's ProjectId. Carrying PreviousDraftId is
    // what lets the pipeline reuse the panels whose source text didn't move and the diff report show what
    // changed. Art style and title default to the previous draft's unless overridden. Returns the new
    // draft's id, or null if the previous draft doesn't exist.
    public async Task<Guid?> CreateNextDraftAsync(
        Guid previousDraftId, string fileId, string fileName, string accessToken,
        string? title, string? artStyle, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var previous = await db.Novels.FirstOrDefaultAsync(n => n.Id == previousDraftId, cancellationToken);
        if (previous is null)
            return null;

        // The newest draft in the project sets the numbering, not necessarily the one passed in — the
        // owner might branch from an older draft, but the next number still follows the latest.
        var highestDraft = await db.Novels
            .Where(n => n.ProjectId == previous.ProjectId)
            .MaxAsync(n => n.DraftNumber, cancellationToken);

        var text = await docs.FetchText(fileId, accessToken, cancellationToken);
        var now = clock.UtcNow;
        var draft = new Novel
        {
            Title = string.IsNullOrWhiteSpace(title) ? previous.Title : title,
            GoogleDocId = fileId,
            GoogleDocName = fileName,
            ArtStyle = string.IsNullOrWhiteSpace(artStyle) ? previous.ArtStyle : artStyle,
            ProjectId = previous.ProjectId,
            DraftNumber = highestDraft + 1,
            PreviousDraftId = previous.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        draft.Source = new SourceText { NovelId = draft.Id, FullText = text, CharacterCount = text.Length };

        db.Novels.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        return draft.Id;
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
        novel.StageAttempts = 0;
        novel.UpdatedAtUtc = clock.UtcNow;

        Revive(await db.Chunks.Where(c => c.NovelId == id).ToListAsync(cancellationToken));
        Revive(await db.DesignRefs.Where(d => d.NovelId == id).ToListAsync(cancellationToken));
        Revive(await db.ChunkAnalyses.Where(a => a.NovelId == id).ToListAsync(cancellationToken));
        Revive(await db.Scenes.Where(s => s.NovelId == id).ToListAsync(cancellationToken));

        // A failed report render resets too, so the revision report can rebuild.
        var analysis = await db.BookAnalyses.FirstOrDefaultAsync(a => a.NovelId == id, cancellationToken);
        if (analysis is not null && analysis.PdfStatus is WorkStatus.Failed or WorkStatus.NeedsRetry)
            analysis.PdfStatus = WorkStatus.Pending;

        await db.SaveChangesAsync(cancellationToken);
    }

    // Data-safety pass run at app start (which covers a device reboot): reconciles the database against
    // what's actually on disk. A unit the DB calls "done" whose file has vanished — an interrupted write,
    // a storage hiccup, a reboot mid-flush — is reset to Pending and the novel is regressed to the stage
    // that rebuilds it, so the pipeline heals the book forward instead of shipping a broken page or PDF.
    // Archived outputs (PdfPath cleared, ArchivedDriveFileId set) are expected to be absent locally and
    // left alone. Returns the number of units healed. Idempotent: a clean project heals nothing.
    public async Task<int> VerifyAndHealOutputsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Cancelled and Failed are left to the user (Retry/rework); healing files under them would quietly
        // resurrect work they deliberately stopped.
        var novelIds = await db.Novels
            .Where(n => n.Stage != ProjectStage.Cancelled && n.Stage != ProjectStage.Failed)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        var healed = 0;
        foreach (var id in novelIds)
            healed += await VerifyNovelOutputs(db, id, cancellationToken);

        if (healed > 0)
            await db.SaveChangesAsync(cancellationToken);
        return healed;
    }

    private async Task<int> VerifyNovelOutputs(StoryboarderDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var novel = await db.Novels.FirstAsync(n => n.Id == novelId, cancellationToken);

        var healed = 0;
        ProjectStage? regressTo = null;
        void Regress(ProjectStage stage)
        {
            if (regressTo is null || stage < regressTo)
                regressTo = stage;
        }

        // Scene images: a rendered evidence image whose file is gone re-renders. A scene deliberately left
        // without an image (a refusal exhausted its retries) has a null ImagePath and is skipped.
        var scenes = await db.Scenes
            .Where(s => s.NovelId == novelId && s.ImageStatus == WorkStatus.Completed && s.ImagePath != null)
            .ToListAsync(cancellationToken);
        foreach (var scene in scenes.Where(s => !imageStore.Exists(s.ImagePath!)))
        {
            scene.ImageStatus = WorkStatus.Pending;
            scene.ImagePath = null;
            scene.Attempts = 0;
            scene.NextAttemptAtUtc = null;
            scene.LastError = null;
            healed++;
            Regress(ProjectStage.RenderingSceneImages);
        }

        // Report: a rendered PDF that's gone (and not archived) re-renders — its content lives in the
        // database, so only the PDF is rebuilt.
        var analysis = await db.BookAnalyses.FirstOrDefaultAsync(a => a.NovelId == novelId, cancellationToken);
        if (analysis is not null && analysis.PdfStatus == WorkStatus.Completed && analysis.PdfPath != null
            && analysis.ArchivedDriveFileId == null && !imageStore.Exists(analysis.PdfPath))
        {
            analysis.PdfStatus = WorkStatus.Pending;
            analysis.PdfPath = null;
            healed++;
            Regress(ProjectStage.ComposingAnalysis);
        }

        // Regress a finished or later-stage novel so the worker re-runs the healed stages; never advance.
        if (regressTo is { } target && (int)novel.Stage > (int)target)
        {
            novel.Stage = target;
            novel.UpdatedAtUtc = clock.UtcNow;
        }

        return healed;
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
            var status = await BuildStatus(db, id, rateLimits, now, cancellationToken);
            if (status is not null)
                statuses.Add(status);
        }
        return statuses;
    }

    public async Task<NovelStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await BuildStatus(db, id, rateLimits, clock.UtcNow, cancellationToken);
    }

    public async Task<string?> GetAnalysisPdfPathAsync(Guid novelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BookAnalyses
            .Where(a => a.NovelId == novelId && a.PdfStatus == WorkStatus.Completed)
            .Select(a => a.PdfPath)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // The consolidated revision report for the in-app reader, folded together exactly as the PDF composer
    // sees it: the story half plus the separately-persisted craft half. Null until the report has been
    // consolidated (the story half is what the reader needs; the craft half folds in as it arrives).
    public async Task<Domain.Analysis.BookAnalysisResult?> GetReportAsync(Guid novelId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.BookAnalyses
            .Where(a => a.NovelId == novelId)
            .Select(a => new { a.ConsolidatedJson, a.CraftJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (stored?.ConsolidatedJson is null)
            return null;

        var report = JsonSerializer.Deserialize<BookAnalysisResult>(stored.ConsolidatedJson, JsonResponse.CaseInsensitive)
                     ?? BookAnalysisResult.Empty;
        var craft = stored.CraftJson is null
            ? CraftReport.Empty
            : JsonSerializer.Deserialize<CraftReport>(stored.CraftJson, JsonResponse.CaseInsensitive) ?? CraftReport.Empty;

        return report.WithCraft(craft);
    }

    // What a draft changed against the one it was made from: the revision-report findings it resolved
    // and newly raised. A first draft — or an unknown id — diffs to None, since there is nothing to
    // compare against.
    public async Task<DraftDiff> GetDraftDiffAsync(Guid draftId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var draft = await db.Novels.FirstOrDefaultAsync(n => n.Id == draftId, cancellationToken);
        if (draft?.PreviousDraftId is not Guid previousId)
            return DraftDiff.None(draftId);

        var currentFindings = await LoadFindingsAsync(db, draftId, cancellationToken);
        var previousFindings = await LoadFindingsAsync(db, previousId, cancellationToken);

        // Findings match on their detail text alone (whitespace/case-normalized). Resolved = the
        // previous draft raised it and this one doesn't; New = the reverse.
        var currentKeys = currentFindings.Select(FindingKey).ToHashSet();
        var previousKeys = previousFindings.Select(FindingKey).ToHashSet();
        var resolved = previousFindings
            .Where(f => !currentKeys.Contains(FindingKey(f)))
            .Select(f => new DiffFinding(FindingChange.Resolved, f.Category, f.Chapter, f.Detail))
            .ToList();
        var added = currentFindings
            .Where(f => !previousKeys.Contains(FindingKey(f)))
            .Select(f => new DiffFinding(FindingChange.New, f.Category, f.Chapter, f.Detail))
            .ToList();

        return new DraftDiff(draftId, previousId, resolved, added);
    }

    // detail text, normalized so trivial whitespace/case differences don't read as a resolved-plus-new
    // pair. The category is part of the key so the same sentence filed under two sections stays distinct.
    private static string FindingKey((string Category, string Chapter, string Detail) f) =>
        f.Category + " " + string.Join(' ', f.Detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    // Flattens a draft's consolidated report into (section, chapter, detail) triples across the four
    // finding sections. Empty when the report hasn't been consolidated yet (an in-flight or art-only draft).
    private static async Task<IReadOnlyList<(string Category, string Chapter, string Detail)>> LoadFindingsAsync(
        StoryboarderDbContext db, Guid novelId, CancellationToken cancellationToken)
    {
        var json = await db.BookAnalyses
            .Where(a => a.NovelId == novelId)
            .Select(a => a.ConsolidatedJson)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(json))
            return [];

        var report = JsonSerializer.Deserialize<BookAnalysisResult>(json, JsonResponse.CaseInsensitive)
                     ?? BookAnalysisResult.Empty;
        var findings = new List<(string, string, string)>();
        void Add(string category, IReadOnlyList<Finding> items)
        {
            foreach (var f in items)
                findings.Add((category, f.Chapter, f.Detail));
        }
        Add("Plot holes", report.PlotHoles);
        Add("Inconsistencies", report.Inconsistencies);
        Add("Inaccuracies", report.Inaccuracies);
        Add("Pacing", report.PacingNotes);
        return findings;
    }

    private static async Task<NovelStatus?> BuildStatus(
        StoryboarderDbContext db, Guid id, RateLimitOptions limits, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null)
            return null;

        var chunks = await Counts(db.Chunks.Where(c => c.NovelId == id), c => c.AnalysisStatus, cancellationToken);
        // Only entities that earn a sheet count toward this phase. Including the one-off entities would
        // report "sheets 12 of 126" for a book where 114 of those were never going to be drawn.
        var sheets = await Counts(
            db.DesignRefs.Where(r => r.NovelId == id && r.NeedsSheet), r => r.ReferenceSheetStatus, cancellationToken);

        var sceneImages = await Counts(db.Scenes.Where(s => s.NovelId == id), s => s.ImageStatus, cancellationToken);

        // The report stage maps every chapter into notes before it consolidates and renders; the
        // per-chapter map is the long part, so surface it as its own progress.
        var analysisNotes = await Counts(db.ChunkAnalyses.Where(a => a.NovelId == id), a => a.Status, cancellationToken);
        var analysisReady = await db.BookAnalyses
            .AnyAsync(a => a.NovelId == id && a.PdfStatus == WorkStatus.Completed, cancellationToken);
        var analysis = new PhaseProgress(analysisReady ? 1 : 0, 1);

        // The pipeline auto-retries transient failures (network drops, rate limits) with backoff, so a
        // manuscript-level LastError is usually a self-healing hiccup, not a stall. It only genuinely needs
        // the user when a once-per-manuscript step has burned through its bounded retry budget.
        var needsAttention = novel.LastError is not null && novel.StageAttempts >= limits.MaxAttempts;

        // Project what the run will cost, but only while abandoning it is still nearly free — before the
        // per-scene images start rendering. The daily image cap is usually what governs.
        Domain.Pipeline.RunEstimate? estimate = null;
        if (novel.Stage is ProjectStage.Created or ProjectStage.Ingesting or ProjectStage.BuildingDesignBible)
        {
            var characters = await db.SourceTexts
                .Where(s => s.NovelId == id)
                .Select(s => s.CharacterCount)
                .FirstOrDefaultAsync(cancellationToken);
            if (characters > 0)
            {
                // Once the bible is consolidated the sheet count is known exactly; before that, fall back
                // to a small ensemble, since only recurring entities are drawn.
                var sheetCount = sheets.Total > 0 ? sheets.Total : 12;
                estimate = Domain.Pipeline.RunEstimator.Estimate(
                    characters, Domain.Text.TextChunker.DefaultTargetChars,
                    limits.ClaudeRequestsPerDay, limits.GeminiRequestsPerDay, sheetCount);
            }
        }

        // Only surface the resume time while it's still in the future; once it passes it's stale, so the
        // card stops showing it (and the worker has presumably resumed).
        var rateLimitedUntil = novel.RateLimitedUntilUtc is { } until && until > now ? novel.RateLimitedUntilUtc : null;

        return new NovelStatus(
            novel.Id, novel.Title, novel.Stage.ToString(), novel.LastError,
            chunks, sheets, sceneImages, analysisNotes, analysis, analysisReady, needsAttention,
            estimate, rateLimitedUntil,
            novel.DraftNumber, novel.PreviousDraftId is not null);
    }

    // A human-readable snapshot of everything that might be wrong: config, last worker run, and per
    // project the stage plus each phase's done/failed/retry counts and a sample of the error messages
    // on the stuck units. Surfaces "sheets 0 of 124, 124 failed: <reason>" without reading the raw log.
    public async Task<string> BuildDiagnosticsReportAsync(DiagnosticsContext context, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var report = new StringBuilder();

        report.AppendLine($"Canonizer diagnostics — {clock.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
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
            report.AppendLine($"  Stage: {novel.Stage}    Updated: {novel.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss}Z");
            if (!string.IsNullOrEmpty(novel.LastError))
                report.AppendLine($"  Novel error: {Truncate(novel.LastError)}");

            await AppendPhase(report, "Chunks", db.Chunks.Where(c => c.NovelId == novel.Id),
                c => c.AnalysisStatus, c => c.LastError, cancellationToken);
            // Scoped to the entities that earn a sheet, matching the live status counts — the one-off
            // entities are in the bible but were never queued to be drawn.
            await AppendPhase(report, "Reference sheets", db.DesignRefs.Where(r => r.NovelId == novel.Id && r.NeedsSheet),
                r => r.ReferenceSheetStatus, r => r.LastError, cancellationToken);
            await AppendPhase(report, "Scene images", db.Scenes.Where(s => s.NovelId == novel.Id),
                s => s.ImageStatus, s => s.LastError, cancellationToken);
            await AppendPhase(report, "Analysis notes", db.ChunkAnalyses.Where(a => a.NovelId == novel.Id),
                a => a.Status, a => a.LastError, cancellationToken);
        }

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
