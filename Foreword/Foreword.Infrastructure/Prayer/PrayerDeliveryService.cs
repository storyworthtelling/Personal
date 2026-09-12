using Microsoft.Extensions.Logging;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Prayer;
using Storyworthtelling.Remarkable;

namespace Foreword.Infrastructure.Prayer;

/// <summary>
/// The prayer morning: read yesterday's sheet back, fold it into the encrypted archive, compose
/// today's two pages, upload, and only then throw yesterday's away.
///
/// <para><b>The ordering is the design.</b> Until the journal page has been transcribed and saved it
/// is the <i>only</i> copy of what the owner wrote — there is no cache, no second file, nothing on
/// the phone. So every step that can fail has to fail before the trash, not after it: a
/// transcription that did not come back, an archive that would not open, an upload that did not
/// land. In each case the sheet stays on the tablet and the morning says so in the log rather than
/// quietly taking the loss.</para>
///
/// <para>Nothing here goes to Google. That is the point of the whole release, and a guard asserts it
/// by reading the repository rather than by trusting this comment.</para>
/// </summary>
public sealed class PrayerDeliveryService(
    IPrayerArchive archive,
    IJournalTranscriber transcriber,
    IPastoralComposer pastoral,
    IPrayerPdfComposer pages,
    IRemarkableClient remarkable,
    Func<string> passphrase,
    ILogger<PrayerDeliveryService> logger)
{
    public async Task Deliver(DateOnly date)
    {
        var key = passphrase();

        var opened = await archive.LoadAsync(key, CancellationToken.None);
        if (opened.Status is PrayerArchiveStatus.WrongPassphrase or PrayerArchiveStatus.Corrupt)
        {
            // Not fatal to the morning — a blank sheet to write on is better than no sheet — but
            // nothing may be read back or saved, because the file that is there cannot be read and
            // must not be written over.
            logger.LogWarning(
                "The prayer archive could not be opened ({Status}); delivering a page with nothing "
                + "on it and leaving yesterday's sheet alone", opened.Status);

            await Upload(date, Compose(date, new PrayerArchive(), PrayerText.Empty).Document);
            return;
        }

        var stored = opened.Archive;

        var (read, yesterdaysId) = await ReadBackYesterday(date);
        if (read.Succeeded)
        {
            stored = Fold(stored, read, date);
        }

        var composed = Compose(date, stored, await PastoralLine(stored, read, date));

        if (!await Upload(date, composed.Document))
        {
            // No new sheet means yesterday's is the only one on the tablet. Keep it.
            logger.LogWarning(
                "Prayer upload failed for {Date}; keeping yesterday's sheet and saving nothing", date);
            return;
        }

        // The strike-through is earned by being printed, so it is marked after the page exists.
        var settled = MarkStrikes(stored, composed.StrikesShown);

        if (!await Save(settled, key, date))
        {
            return;
        }

        if (yesterdaysId is not null && read.Succeeded)
        {
            await Trash(yesterdaysId);
        }
    }

    private async Task<(JournalRead Read, string? DocId)> ReadBackYesterday(DateOnly date)
    {
        var yesterday = date.AddDays(-1);

        foreach (var title in PrayerTitles.ToReadBack(yesterday))
        {
            try
            {
                if (await remarkable.DownloadByTitle(title) is not { } doc)
                {
                    continue;
                }

                return (await transcriber.Read(doc.PdfBytes, yesterday), doc.Id);
            }
            catch (Exception ex)
            {
                // Best-effort, like The Day's read-back: a failure here must not cost today's sheet.
                logger.LogWarning(ex, "Prayer read-back failed for {Title}", title);
            }
        }

        return (JournalRead.Nothing, null);
    }

    /// <summary>
    /// What the sheet said, folded in: the free space becomes an entry dated to <b>the page's</b>
    /// day, and each line under the strip becomes something held from this morning.
    /// </summary>
    private static PrayerArchive Fold(PrayerArchive stored, JournalRead read, DateOnly date)
    {
        var journal = stored.Journal.ToList();
        if (!read.Journalled.IsEmpty)
        {
            // Dated to the day it was written on, not to today: it is what From before selects on,
            // so dating it wrong files a memory under the wrong morning.
            journal.Add(new JournalEntry { Date = date.AddDays(-1), Text = read.Journalled });
        }

        var requests = stored.Requests.ToList();
        foreach (var added in read.Added)
        {
            requests.Add(new PrayerRequest
            {
                Id = Guid.NewGuid().ToString("n"),
                Text = added,
                Held = date,
            });
        }

        return stored with { Journal = journal, Requests = requests };
    }

    private (byte[] Document, IReadOnlyList<string> StrikesShown) Compose(
        DateOnly date, PrayerArchive stored, PrayerText lead)
    {
        var printed = PrayerPrinting.ForToday(stored.Requests, date);

        var document = new PrayerDocument
        {
            Date = date,
            Lead = lead,
            Holding = printed.Rows,
            Rotations = RotationPick.ForAll(stored.Rotations, date),
            FromBefore = Resurfacing.From(stored.Journal, date),
        };

        return (pages.Compose(document), printed.StrikesShown);
    }

    private Task<PrayerText> PastoralLine(PrayerArchive stored, JournalRead read, DateOnly date)
    {
        var held = stored.Requests.Where(request => request.Answered is null).ToArray();

        var brief = new PastoralBrief
        {
            Held = held.Length,
            AnsweredThisMorning = stored.Requests.Count(
                request => request.Answered is not null && !request.StrikePrinted),
            LongestHeld = held.Length == 0
                ? string.Empty
                : HoldDuration.Describe(held.Min(request => request.Held), date),
            MostRecentEntry = read.Journalled.IsEmpty
                ? MostRecent(stored.Journal)
                : read.Journalled,
        };

        return pastoral.Compose(brief, CancellationToken.None);
    }

    private static PrayerText MostRecent(IReadOnlyList<JournalEntry> journal) =>
        journal.Count == 0
            ? PrayerText.Empty
            : journal.OrderByDescending(entry => entry.Date).First().Text;

    private static PrayerArchive MarkStrikes(PrayerArchive stored, IReadOnlyList<string> shown)
    {
        if (shown.Count == 0)
        {
            return stored;
        }

        return stored with
        {
            Requests = [.. stored.Requests.Select(request =>
                shown.Contains(request.Id) ? request with { StrikePrinted = true } : request)],
        };
    }

    private async Task<bool> Upload(DateOnly date, byte[] document)
    {
        var title = PrayerTitles.Document(date);

        try
        {
            await remarkable.Upload(title, document);
            logger.LogInformation("Uploaded {Title}", title);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Prayer upload failed for {Title}", title);
            return false;
        }
    }

    private async Task<bool> Save(PrayerArchive settled, string key, DateOnly date)
    {
        try
        {
            await archive.SaveAsync(settled, key, CancellationToken.None);
            return true;
        }
        catch (PrayerArchiveLockedException ex)
        {
            // The archive refusing to be overwritten. Yesterday's sheet stays where it is, because
            // what was on it has nowhere to go.
            logger.LogWarning(ex, "The prayer archive was not written for {Date}", date);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The prayer archive could not be saved for {Date}", date);
            return false;
        }
    }

    private async Task Trash(string docId)
    {
        try
        {
            await remarkable.MoveToTrash(docId);
            logger.LogInformation("Moved yesterday's prayer sheet {Id} to trash", docId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not trash yesterday's prayer sheet {Id}", docId);
        }
    }
}
