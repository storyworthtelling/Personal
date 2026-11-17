using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Folio.Domain.Model;
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
    /// The bible's own data in Drive, plain JSON beside the Doc and rewritten with it (owner's request): what
    /// a new phone restores from. The Doc is for reading; this is for putting the bible back whole.
    /// </summary>
    public static string DataName(string series) => $"{series} — Series bible (data).json";

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
            record.DriveDataId = await delivery.WriteDriveText(
                record.DriveDataId, folder ?? string.Empty, DataName(series), "application/json",
                SeriesBibleCodec.Write(bible), cancellationToken);
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

    /// <summary>
    /// Puts a series' bible back from Drive on a phone that has none for it (a reset). Found by its data
    /// file's name, under the series' name or, failing that, its first book's title; rebound to this phone's
    /// books by title; the owner's corrections come with it. The Doc and data are remembered so the next
    /// backup rewrites them. A phone that has its bible asks Drive nothing, and nothing is written from a copy
    /// that will not read or a Drive that will not answer.
    /// </summary>
    public async Task<BibleRestore> RestoreAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.SeriesBibles.FirstOrDefaultAsync(bible => bible.SeriesId == seriesId, cancellationToken);
        if (record is not null && (!SeriesBibleCodec.TryRead(record.Json, out var held) || held.Entries.Count > 0))
            return BibleRestore.NotNeeded;
        if (delivery is not { CanArchiveToDrive: true })
            return BibleRestore.Unavailable;

        var drafts = await db.Novels
            .Where(novel => novel.SeriesId == seriesId && novel.DeletedAtUtc == null)
            .Select(novel => new { novel.ProjectId, novel.Title, novel.SeriesName, novel.SeriesSequence, novel.DraftNumber })
            .ToListAsync(cancellationToken);
        var local = drafts.GroupBy(draft => draft.ProjectId)
            .Select(book => book.OrderByDescending(draft => draft.DraftNumber).First())
            .Select(book => new SeriesBook(book.ProjectId, book.Title, book.SeriesSequence))
            .OrderBy(book => book.Written).ToList();
        var names = new[]
            {
                drafts.Select(draft => draft.SeriesName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                local.FirstOrDefault()?.Title,
            }
            .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).Distinct().ToList();

        try
        {
            foreach (var series in names)
            {
                if (await delivery.FindDriveFile(DataName(series), cancellationToken) is not { } dataId)
                    continue;

                if (!SeriesBibleCodec.TryRead(await delivery.ReadDriveText(dataId, cancellationToken), out var bible) || bible.Entries.Count == 0)
                {
                    logger.LogWarning("The series bible found in Drive would not read; it was left as it is and nothing was restored");
                    return BibleRestore.Unreadable;
                }

                var docId = await delivery.FindDriveFile(DocName(series), cancellationToken);
                if (record is null)
                {
                    record = new SeriesBibleRecord { SeriesId = seriesId };
                    db.SeriesBibles.Add(record);
                }
                record.Json = SeriesBibleCodec.Write(bible.Rebind(local));
                record.UpdatedAtUtc = clock.UtcNow;
                record.DriveDataId = dataId;
                record.DriveDocId = docId;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Restored a series bible from Drive: {Entries} entries across {Books} books",
                    bible.Entries.Count, bible.Books.Count);
                return BibleRestore.Restored;
            }
            return BibleRestore.NotFound;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Drive could not be asked for a series bible to restore");
            return BibleRestore.Unavailable;
        }
    }
}

/// <summary>What a restore of a series bible from Drive came to.</summary>
public enum BibleRestore
{
    NotNeeded,
    Restored,
    NotFound,
    Unreadable,
    Unavailable,
}
