using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Notices;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Delivery;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The morning read-back (C-2.2.3, D87): for each document delivered and not yet let go of, fetch the tablet's PDF of
/// it, have the ink read box by box, resolve it by the book's rules (<see cref="InkResolver"/>), save the result and the
/// ledger, put any question on the Waiting page, and only then trash the page.
///
/// <para><b>The order is the rule.</b> download, read, resolve, persist, ask, trash. Every step that can fail comes before
/// the trash, so a failure at any of them leaves the page on the tablet and the document outstanding, and the next
/// morning tries again. A failure after the result is saved (the question, the trash) is remembered
/// (<see cref="IExpectedDocuments.MarkSaved"/>), so the next morning does not read the page a second time.</para>
///
/// <para><b>A page that was not read is not a page that was empty.</b> A page the read said nothing about, a box it left
/// out, a page the tablet does not have: none of them is silence, none touches the ledger, and none is trashed. A skip is
/// only ever written from a read that was sure every box was empty (the resolver's rule), and, until some page has
/// shown that pen ink comes through the cloud's PDF at all (<see cref="IInkProof"/>), not even then: a document with no
/// ink anywhere is reported as <see cref="DocumentReadBack.NoMarksInCloud"/> and kept (R3).</para>
///
/// <para><b>One bad document does not stop the others.</b> A failure is caught where it happens, named by the step it
/// happened in, and kept with its exception (stack and all) in the report; the caller logs its type. A caller's own
/// cancellation is not a failure and is never caught.</para>
///
/// <para><b>A workout is read only once its day is over.</b> One whose day is today or later is left alone (not read, not
/// settled, not trashed): its page is blank because the session has not happened, and a blank is a skip once ink is proven
/// (<see cref="ReadBackPolicy.IsDue"/>). The day is the date on the owner's own clock. A meals sheet is read through its
/// own day, since its deadline rather than the date says what its silence means.</para>
///
/// <para>A document that has not settled in <see cref="ReadBackPolicy.GiveUpAfterDays"/> is retired and its page left
/// where it is. The digest is not read at all, only trashed after <see cref="ReadBackPolicy.DigestDays"/>.</para>
/// </summary>
public sealed class ReadBackService(
    IExpectedDocuments expected,
    IAnnotatedPageSource tablet,
    IInkVision vision,
    IResolutionStore results,
    ILedgerStore ledger,
    IInkProof proof,
    INoticeBoardAccess notices,
    IReadBackLog log,
    Func<DateTimeOffset> clock,
    IReadBackSettled? settled = null,
    IPlannerNotes? plannerNotes = null)
{
    private readonly record struct Step<T>(T? Value, DocumentReadBack.Failed? Failure);

    /// <summary>Handles every outstanding document, oldest first, and says what became of each.</summary>
    public async Task<ReadBackReport> Run(CancellationToken cancellation = default)
    {
        var now = clock();
        var today = DateOnly.FromDateTime(now.DateTime);
        var outcomes = new List<DocumentReadBack>();

        // A workout for today (or a later day) has not been done yet: read now it is a blank, and once ink is proven a blank
        // settles a skip for a session that has not happened. A workout is read only once its day has ended on the owner's clock.
        var outstanding = expected.Outstanding()
            .Where(entry => ReadBackPolicy.IsDue(entry.Document.Kind, entry.Document.Day, today))
            .OrderBy(entry => entry.Document.Day)
            .ThenBy(entry => entry.Document.Name, StringComparer.Ordinal);
        foreach (var entry in outstanding)
        {
            cancellation.ThrowIfCancellationRequested();
            var outcome = await Handle(entry, now, today, cancellation);
            log.Outcome(ReadBackTrail.LogPhrase(outcome));
            outcomes.Add(outcome);
        }

        return new ReadBackReport(outcomes);
    }

    private async Task<DocumentReadBack> Handle(OutstandingDocument entry, DateTimeOffset now, DateOnly today, CancellationToken cancellation)
    {
        var document = entry.Document;
        if (ReadBackPolicy.HasGivenUp(document.Day, today))
        {
            var retired = Now(document.Name, ReadBackStage.Persist, () => expected.Retire(document.Name), cancellation);
            return Otherwise(retired, new DocumentReadBack.GaveUp(document.Name));
        }

        if (!document.ReadsInk)
        {
            return await Digest(document, today, cancellation);
        }

        if (entry.Saved)
        {
            var loaded = Attempt(document.Name, ReadBackStage.Persist, () => results.Load(document.Name), cancellation);
            if (loaded.Failure is not null)
            {
                return loaded.Failure;
            }

            if (loaded.Value is { } saved)
            {
                return await FinishSaved(document, saved, now, cancellation);
            }

            // The result this page was waiting on has gone; the page is the only copy of the answer, so it is read again.
        }

        return await ReadAndSettle(document, now, today, cancellation);
    }

    // ---- the digest: only ever trashed ----

    private async Task<DocumentReadBack> Digest(ExpectedDocument document, DateOnly today, CancellationToken cancellation)
    {
        // Owner decision amending C-2.2.3: the planner's four ruled lines are read once, from Sunday, and the page still goes on day three. A failure here is the
        // read's own and is tried again next run; it does not stop the trash rule below from being reached on a later day.
        if (document.ReadsPlannerNotes && plannerNotes is not null
            && DigestPlannerNotes.IsDue(document.Day, today) && !plannerNotes.Has(document.Person, document.Day))
        {
            if (await ReadPlannerNotes(document, plannerNotes, cancellation) is { } failed)
            {
                return failed;
            }
        }

        if (!ReadBackPolicy.DigestIsDue(document.Day, today))
        {
            return new DocumentReadBack.DigestKept(document.Name);
        }

        var located = await AttemptAsync(document.Name, ReadBackStage.Trash, () => tablet.Locate(document.Name), cancellation);
        if (located.Failure is not null)
        {
            return located.Failure;
        }

        if (located.Value is null)
        {
            // Already gone from the tablet: nothing left to trash, and nothing left to wait for.
            var retired = Now(document.Name, ReadBackStage.Trash, () => expected.Retire(document.Name), cancellation);
            return Otherwise(retired, new DocumentReadBack.NotOnTablet(document.Name));
        }

        return await Trash(document, located.Value, new DocumentReadBack.DigestTrashed(document.Name), cancellation);
    }

    // Reads only the four note lines (the document names no other target), keeps what was written (the rules, and so the resolutions, are in the lines' order), and marks the digest read. Null when it went through.
    private async Task<DocumentReadBack.Failed?> ReadPlannerNotes(ExpectedDocument document, IPlannerNotes sink, CancellationToken cancellation)
    {
        var name = document.Name;
        var downloaded = await AttemptAsync(name, ReadBackStage.Download, () => tablet.Download(name), cancellation);
        if (downloaded.Failure is not null)
        {
            return downloaded.Failure;
        }

        if (downloaded.Value is not { } page)
        {
            // Not on the tablet: nothing to read, and the trash rule below finds it gone in its own time.
            return null;
        }

        var readStep = await AttemptAsync(name, ReadBackStage.Read, () => vision.Read(document, page.PdfBytes, cancellation), cancellation);
        if (readStep.Failure is not null)
        {
            return readStep.Failure;
        }

        var read = readStep.Value!;
        var notesPage = document.Pages[0];
        if (!read.PageFor(notesPage.Number).WasRead)
        {
            return null;
        }

        var resolved = Attempt(name, ReadBackStage.Resolve, () => Resolve(document, read, clock()), cancellation);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var notes = resolved.Value!.SelectMany(item => item.Resolutions).OfType<InkResolution.Needed>()
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .Select(item => item.Text!.Trim())
            .ToList();
        return Now(name, ReadBackStage.Persist, () => sink.Save(document.Person, document.Day, notes), cancellation);
    }

    // ---- a result already saved: raise its questions again, then trash ----

    private async Task<DocumentReadBack> FinishSaved(ExpectedDocument document, DocumentResult saved, DateTimeOffset now, CancellationToken cancellation)
    {
        var asked = Ask(document, saved.All, now, inkSeen: false, cancellation);
        if (asked is not null)
        {
            return asked;
        }

        var located = await AttemptAsync(document.Name, ReadBackStage.Trash, () => tablet.Locate(document.Name), cancellation);
        if (located.Failure is not null)
        {
            return located.Failure;
        }

        var settled = new DocumentReadBack.Settled(document.Name, saved.Pages.Count, saved.Questions.Count);
        if (located.Value is null)
        {
            // The page is already gone (trashed by an earlier run whose last step failed, or by the owner).
            var retired = Now(document.Name, ReadBackStage.Trash, () => expected.Retire(document.Name), cancellation);
            return Otherwise(retired, settled);
        }

        return await Trash(document, located.Value, settled, cancellation);
    }

    // ---- a page to read ----

    private async Task<DocumentReadBack> ReadAndSettle(ExpectedDocument document, DateTimeOffset now, DateOnly today, CancellationToken cancellation)
    {
        var name = document.Name;

        var downloaded = await AttemptAsync(name, ReadBackStage.Download, () => tablet.Download(name), cancellation);
        if (downloaded.Failure is not null)
        {
            return downloaded.Failure;
        }

        if (downloaded.Value is not { } page)
        {
            return new DocumentReadBack.NotOnTablet(name);
        }

        var readStep = await AttemptAsync(name, ReadBackStage.Read, () => vision.Read(document, page.PdfBytes, cancellation), cancellation);
        if (readStep.Failure is not null)
        {
            return readStep.Failure;
        }

        var read = readStep.Value!;
        var readPages = document.Pages.Where(expectedPage => read.PageFor(expectedPage.Number).WasRead).ToList();
        if (readPages.Count == 0)
        {
            return new DocumentReadBack.Unfinished(name, read.PageFor(document.Pages[0].Number).WhyNotRead);
        }

        // R3: the cloud's PDF may simply not carry the pen. No ink anywhere, and none ever proven, is not yet silence.
        var proofStep = Attempt(name, ReadBackStage.Persist, () => proof.Current, cancellation);
        if (proofStep.Failure is not null)
        {
            return proofStep.Failure;
        }

        var wasProven = proofStep.Value!;
        var anyBoxMarked = readPages.Any(expectedPage => HasAnyMark(expectedPage, read));

        // The read saw pen ink on the pages and not one box is marked: the ink may be a tick it could not put in a box, or the
        // owner writing about the session instead of ticking it. Either way "everything is empty" is not what it saw, so the
        // session is not called skipped; the page stays and is read again.
        if (read.PenStrokes == PenStrokes.Seen && !anyBoxMarked)
        {
            return new DocumentReadBack.Unfinished(name, "Pen ink was seen on the pages but no box was marked, so nothing was settled.");
        }

        if (!wasProven.IsEstablished && !anyBoxMarked)
        {
            var raised = Attempt(name, ReadBackStage.Ask, () => notices.With(board => ReadBackNotices.NoPenMarks(board, now)), cancellation);
            return Otherwise(raised.Failure, new DocumentReadBack.NoMarksInCloud(name));
        }

        // The ink was proven this very morning, and a blank page read now may be one whose ink the cloud had not yet caught up
        // with. It waits one more morning before it is called a skip and its page goes.
        if (wasProven.IsEstablished && wasProven.EstablishedOn == today && !anyBoxMarked)
        {
            return new DocumentReadBack.Unfinished(name, "Pen ink has only just been proven to come through, so a blank page is kept one more morning.");
        }

        // A sighting is ink in this document that the read is sure of, or its word "seen" with a mark behind it (the word with no
        // mark behind it was turned away above).
        var sawInk = read.PenStrokes == PenStrokes.Seen || readPages.Any(expectedPage => HasConfidentMark(expectedPage, read));

        var resolved = Attempt(name, ReadBackStage.Resolve, () => Resolve(document, read, now), cancellation);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var pages = resolved.Value!;
        var result = new DocumentResult(name, document.Kind, document.Day, document.Person, now, pages);
        if (ReadBackPolicy.WhyKept(pages) is { } why)
        {
            // The page stays, but what was already said on it was said on time: a hook that records answers is told before the page is put back.
            var observed = Now(name, ReadBackStage.Persist, () => (settled as IReadBackObserved)?.Observed(document, result), cancellation);
            return Otherwise(observed, new DocumentReadBack.Unfinished(name, why));
        }

        var saved = Now(name, ReadBackStage.Persist, () => Persist(document, result, sawInk ? new InkSighting(name, today) : null), cancellation);
        if (saved is not null)
        {
            return saved;
        }

        var asked = Ask(document, result.All, now, sawInk, cancellation);
        if (asked is not null)
        {
            return asked;
        }

        var done = new DocumentReadBack.Settled(name, pages.Count, result.Questions.Count);
        return await Trash(document, page.Id, done, cancellation);
    }

    private static List<PageResolutions> Resolve(ExpectedDocument document, InkDocumentRead read, DateTimeOffset now) =>
        document.Pages
            .Select(page => new PageResolutions(
                page.Number, new InkResolver(page.Readout).Resolve(read.PageFor(page.Number), page.Rules, now)))
            .ToList();

    // Everything that must be safe before the page goes. The ledger is looked at first so a file that cannot be read
    // is found before anything is written; saving again after a failure part-way is harmless, since both writes replace.
    private void Persist(ExpectedDocument document, DocumentResult result, InkSighting? sighting)
    {
        SkipLedgerRead? current = null;
        if (document.Marks is not null)
        {
            current = ledger.Load();
            // A file that is not a ledger at all is not written over. A ledger that is only partly readable is: the entries this
            // version cannot read (a kind or outcome it does not know) ride inside the ledger as their raw JSON and are written
            // back as they were, so recording a skip does not cost the only copy of them.
            if (current.WholeFileUnreadable)
            {
                throw new LedgerUnreadableException();
            }
        }

        results.Save(result);

        if (document.Marks is not null && current is not null)
        {
            var next = current.Ledger.Settle(document.Marks, result.All);
            if (!ReferenceEquals(next, current.Ledger))
            {
                ledger.Save(next);
            }
        }

        // What the pages taught: told after the skip ledger and before the document is marked saved, so a failure keeps the page
        // and the next morning tries again (learning from the same day twice changes nothing).
        settled?.Settled(document, result);

        if (sighting is not null)
        {
            proof.Record(sighting);
        }

        expected.MarkSaved(document.Name);
    }

    // Questions go to the Waiting page; a mark seen in this document takes the "no pen marks" notice away, since that notice says
    // none ever has been, which is no longer true (the proof itself waits for a second document).
    private DocumentReadBack.Failed? Ask(
        ExpectedDocument document, IReadOnlyList<InkResolution> resolutions, DateTimeOffset now, bool inkSeen, CancellationToken cancellation)
    {
        if (!inkSeen && !resolutions.OfType<InkResolution.NeedsAsking>().Any())
        {
            return null;
        }

        return Now(document.Name, ReadBackStage.Ask, () => notices.With(board =>
        {
            InkAsks.Raise(board, resolutions, now);
            if (inkSeen)
            {
                ReadBackNotices.PenMarksSeen(board);
            }

            return true;
        }), cancellation);
    }

    private async Task<DocumentReadBack> Trash(ExpectedDocument document, string tabletId, DocumentReadBack done, CancellationToken cancellation)
    {
        var trashed = await AttemptAsync(document.Name, ReadBackStage.Trash, async () =>
        {
            await tablet.Trash(tabletId);
            expected.Retire(document.Name);
            return true;
        }, cancellation);
        return Otherwise(trashed.Failure, done);
    }

    // ---- what the read saw ----

    private static bool HasAnyMark(ExpectedPage page, InkDocumentRead read) =>
        page.AskedTargets.Any(target => read.PageFor(page.Number).For(target.Id)?.Marked == true);

    private static bool HasConfidentMark(ExpectedPage page, InkDocumentRead read) =>
        page.AskedTargets.Any(target => read.PageFor(page.Number).For(target.Id) is { Marked: true } mark && mark.Confidence >= InkResolver.MinimumConfidence);

    // ---- failures, named where they happen ----

    private static async Task<Step<T>> AttemptAsync<T>(string name, ReadBackStage stage, Func<Task<T>> work, CancellationToken cancellation)
    {
        try
        {
            return new Step<T>(await work(), null);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            return new Step<T>(default, new DocumentReadBack.Failed(name, stage, failure));
        }
    }

    private static Step<T> Attempt<T>(string name, ReadBackStage stage, Func<T> work, CancellationToken cancellation)
    {
        try
        {
            return new Step<T>(work(), null);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            return new Step<T>(default, new DocumentReadBack.Failed(name, stage, failure));
        }
    }

    private static DocumentReadBack Otherwise(DocumentReadBack.Failed? failure, DocumentReadBack done) => failure is null ? done : failure;

    // A step with no result: the failure, or null when it went through.
    private static DocumentReadBack.Failed? Now<T>(string name, ReadBackStage stage, Func<T> work, CancellationToken cancellation) =>
        Attempt(name, stage, work, cancellation).Failure;

    private static DocumentReadBack.Failed? Now(string name, ReadBackStage stage, Action work, CancellationToken cancellation) =>
        Attempt(name, stage, () =>
        {
            work();
            return true;
        }, cancellation).Failure;
}
