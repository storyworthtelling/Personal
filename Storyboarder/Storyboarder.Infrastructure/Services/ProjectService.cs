using Microsoft.EntityFrameworkCore;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Model;
using Storyboarder.Domain.Time;
using Storyboarder.Infrastructure.Persistence;

namespace Storyboarder.Infrastructure.Services;

// Application service backing the controllers: creates a novel from a picked Google Doc and reports
// progress. Controllers go through this rather than touching the DbContext directly.
public sealed class ProjectService
{
    private readonly IDbContextFactory<StoryboarderDbContext> dbContextFactory;
    private readonly IGoogleDocsClient docs;
    private readonly IClock clock;

    public ProjectService(
        IDbContextFactory<StoryboarderDbContext> dbContextFactory, IGoogleDocsClient docs, IClock clock)
    {
        this.dbContextFactory = dbContextFactory;
        this.docs = docs;
        this.clock = clock;
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

    public async Task<IReadOnlyList<NovelStatus>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Novels.OrderByDescending(n => n.CreatedAtUtc).Select(n => n.Id).ToListAsync(cancellationToken);
        var statuses = new List<NovelStatus>(ids.Count);
        foreach (var id in ids)
        {
            var status = await BuildStatus(db, id, cancellationToken);
            if (status is not null)
                statuses.Add(status);
        }
        return statuses;
    }

    public async Task<NovelStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await BuildStatus(db, id, cancellationToken);
    }

    public async Task<string?> GetVolumePdfPathAsync(Guid novelId, int number, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Volumes
            .Where(v => v.NovelId == novelId && v.Number == number && v.PdfStatus == WorkStatus.Completed)
            .Select(v => v.PdfPath)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<NovelStatus?> BuildStatus(StoryboarderDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null)
            return null;

        var chunks = await Counts(db.Chunks.Where(c => c.NovelId == id), c => c.AnalysisStatus, cancellationToken);
        var sheets = await Counts(db.DesignRefs.Where(r => r.NovelId == id), r => r.ReferenceSheetStatus, cancellationToken);

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

        return new NovelStatus(
            novel.Id, novel.Title, novel.Stage.ToString(), novel.LastError,
            chunks, sheets, panels, pages, volumes, volumeList);
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
