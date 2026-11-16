using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Folio.Domain.Series;
using Folio.Domain.Time;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Persistence;
using Storyworthtelling.Paper;

namespace Folio.Infrastructure.Services;

/// <summary>
/// Keeps a series bible's backup in Drive (owner's request): one plain, unencrypted Google Doc per series,
/// rewritten in place each time the bible changes, after a book is read into it or the owner corrects it.
/// Drive keeps each write as a version in the doc's own history, so the history shows what each book
/// and each correction changed; the doc's second line says which.
///
/// <para>The doc's id is kept on the series' bible, so it stays one document wherever the series' books
/// sit in Drive. It is first made beside the book being backed up, or the newest series book that has a
/// Drive folder. Best-effort, like the rest of Drive: a write that fails says so and is tried again at the
/// bible's next change, never thrown into the review or the screen.</para>
/// </summary>
public sealed class SeriesBibleBackup(
    IDbContextFactory<FolioDbContext> dbContextFactory,
    IPaperDelivery? delivery,
    IOptions<ArchiveOptions> archiveOptions,
    IClock clock,
    ILogger logger)
{
    /// <summary>The doc's name in Drive. Never dated: it is the same document, version after version.</summary>
    public static string DocName(string series) => $"{series} — Series bible";

    /// <summary>
    /// Writes the series' bible to its doc, and answers whether it did. <paramref name="occasion"/> is what
    /// prompted this version ("after The Salt Road, draft 2"); <paramref name="folderId"/> is where a doc
    /// not yet made goes, when the caller knows.
    /// </summary>
    public async Task<bool> WriteAsync(Guid seriesId, string occasion, string? folderId, CancellationToken cancellationToken)
    {
        if (!archiveOptions.Value.Enabled || delivery is not { CanArchiveToDrive: true })
            return false;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.SeriesBibles.FirstOrDefaultAsync(bible => bible.SeriesId == seriesId, cancellationToken);
        if (record is null || !SeriesBibleCodec.TryRead(record.Json, out var bible) || bible.Entries.Count == 0)
            return false;

        var books = await db.Novels
            .Where(novel => novel.SeriesId == seriesId && novel.DeletedAtUtc == null)
            .Select(novel => new { novel.ProjectId, novel.DraftNumber, novel.SeriesName, novel.PlacementJson, novel.ArchiveFolderId, novel.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var series = books.Select(book => book.SeriesName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                     ?? bible.Books.FirstOrDefault()?.Title ?? "The series";
        var placements = books
            .GroupBy(book => book.ProjectId)
            .Select(drafts => drafts.OrderByDescending(draft => draft.DraftNumber).First())
            .Select(book => (book.ProjectId, Placement: StoryPlacementCodec.Read(book.PlacementJson)))
            .Where(book => book.Placement is not null)
            .ToDictionary(book => book.ProjectId, book => book.Placement!);
        var folder = folderId
                     ?? books.Where(book => book.ArchiveFolderId != null).OrderByDescending(book => book.CreatedAtUtc)
                         .Select(book => book.ArchiveFolderId).FirstOrDefault();
        if (record.DriveDocId is null && folder is null)
            return false;

        try
        {
            record.DriveDocId = await delivery.WriteDriveDoc(
                record.DriveDocId, folder ?? string.Empty, DocName(series),
                BibleDocumentText.For(series, bible, placements, occasion), cancellationToken);
            record.BackedUpAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Backed up the series bible to its Drive doc ({Occasion})", occasion);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The series bible's Drive doc was not written ({Occasion}); it is tried again at the next change", occasion);
            return false;
        }
    }
}
