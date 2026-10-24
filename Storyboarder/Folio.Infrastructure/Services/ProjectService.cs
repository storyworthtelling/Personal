using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Folio.Domain.Analysis;
using Folio.Domain.External;
using Folio.Domain.Imaging;
using Folio.Domain.Model;
using Folio.Domain.Pipeline;
using Folio.Domain.Readiness;
using Folio.Domain.Reading;
using Folio.Domain.Text;
using Folio.Domain.Time;
using Folio.Domain.Voice;
using Folio.Infrastructure.Configuration;
using Storyworthtelling.Voice;
using Folio.Infrastructure.External;
using Folio.Infrastructure.Persistence;

namespace Folio.Infrastructure.Services;

// App-level state the diagnostics report needs but that lives in the platform head (MAUI Preferences),
// passed in so the service stays platform-free.
public sealed record DiagnosticsContext(
    bool ClaudeKeySet, bool GeminiKeySet, string GoogleAccount,
    string LastRunOutcome, DateTimeOffset? LastRunAtUtc, string? MigrationError);

// Application service backing the controllers: creates a novel from a picked Google Doc and reports
// progress. Controllers go through this rather than touching the DbContext directly.
public sealed class ProjectService
{
    private readonly IDbContextFactory<FolioDbContext> dbContextFactory;
    private readonly IGoogleDocsClient docs;
    private readonly IImageStore imageStore;
    private readonly IClock clock;
    private readonly RateLimitOptions rateLimits;

    // Optional, like the pipeline's: a head with no Drive access still deletes books. The voice is a
    // nicety and never a gate on the owner's own instruction.
    private readonly IVoiceProfileStore? voiceStore;
    private readonly IVoiceClient? voiceClient;

    public ProjectService(
        IDbContextFactory<FolioDbContext> dbContextFactory, IGoogleDocsClient docs,
        IImageStore imageStore, IClock clock, IOptions<RateLimitOptions> rateLimitOptions,
        IVoiceProfileStore? voiceStore = null, IVoiceClient? voiceClient = null)
    {
        this.voiceClient = voiceClient;
        this.dbContextFactory = dbContextFactory;
        this.docs = docs;
        this.imageStore = imageStore;
        this.clock = clock;
        rateLimits = rateLimitOptions.Value;
        this.voiceStore = voiceStore;
    }

    public async Task<Guid> CreateFromDocAsync(
        string fileId, string fileName, string accessToken, string? title, string? artStyle, CancellationToken cancellationToken)
    {
        var text = await docs.FetchText(fileId, accessToken, cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.UtcNow;
        var novel = new Novel
        {
            Title = string.IsNullOrWhiteSpace(title) ? ManuscriptTitle.FromFileName(fileName) : title,
            GoogleDocId = fileId,
            GoogleDocName = fileName,
            ArtStyle = string.IsNullOrWhiteSpace(artStyle) ? ArtStyles.Default : artStyle,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        // First draft of a new project: the project is identified by this draft's own id.
        novel.ProjectId = novel.Id;
        novel.Source = new SourceText { NovelId = novel.Id, FullText = text, CharacterCount = text.Length };
        novel.WordCount = Words(text);

        db.Novels.Add(novel);
        await db.SaveChangesAsync(cancellationToken);
        return novel.Id;
    }

    // Adds the next draft to an existing project: the owner re-picks a doc (the revised manuscript) and
    // gets a fresh conversion that shares the previous draft's ProjectId. Carrying PreviousDraftId is
    // what lets the pipeline reuse the panels whose source text didn't move and the diff report show what
    // changed. Art style defaults to the previous draft's unless overridden; the title comes from the newly
    // picked manuscript's own name (each draft is titled from its own file). Returns the new draft's id, or
    // null if the previous draft doesn't exist.
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
            Title = string.IsNullOrWhiteSpace(title) ? ManuscriptTitle.FromFileName(fileName) : title,
            GoogleDocId = fileId,
            GoogleDocName = fileName,
            ArtStyle = string.IsNullOrWhiteSpace(artStyle) ? previous.ArtStyle : artStyle,
            ProjectId = previous.ProjectId,
            DraftNumber = highestDraft + 1,
            PreviousDraftId = previous.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        // Later drafts of a linked book stay linked to the prior book — carry the series fields forward.
        draft.SeriesId = previous.SeriesId;
        draft.PreviousBookId = previous.PreviousBookId;
        draft.SeriesSequence = previous.SeriesSequence;
        draft.Source = new SourceText { NovelId = draft.Id, FullText = text, CharacterCount = text.Length };
        draft.WordCount = Words(text);

        db.Novels.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        return draft.Id;
    }

    // Starts book N of a series: the owner picks a fresh manuscript that continues an existing book, and gets a
    // NEW project (its own draft chain) linked to the prior book. The link is what lets the pipeline reuse the
    // prior book's canon — its design bible for visual consistency, its continuity summary and open findings for
    // the cross-book continuity read. Unlike a next draft (same ProjectId), a next book gets ProjectId = its own
    // Id, DraftNumber 1, and no PreviousDraftId — it is a new book, not a revision. Returns the new book's id, or
    // null if the prior book's draft doesn't exist.
    public async Task<Guid?> CreateNextBookAsync(
        Guid previousBookDraftId, string fileId, string fileName, string accessToken,
        string? title, string? artStyle, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var previous = await db.Novels.FirstOrDefaultAsync(n => n.Id == previousBookDraftId, cancellationToken);
        if (previous is null)
            return null;

        // Establish the series id: the prior book's if it already belongs to a series, else its ProjectId
        // becomes the series id — book one is now the first book of a new series, so backfill every one of its
        // drafts with that id at sequence 1.
        var seriesId = previous.SeriesId ?? previous.ProjectId;
        if (previous.SeriesId is null)
        {
            var bookOneDrafts = await db.Novels
                .Where(n => n.ProjectId == previous.ProjectId)
                .ToListAsync(cancellationToken);
            foreach (var draft in bookOneDrafts)
            {
                draft.SeriesId = seriesId;
                draft.SeriesSequence = 1;
            }
        }

        // The canon source is the prior book's latest-completed draft (fall back to its highest draft number if
        // none has finished yet) — the most finished bible/continuity/findings the sequel can reuse.
        var priorBookDrafts = await db.Novels
            .Where(n => n.ProjectId == previous.ProjectId)
            .Select(n => new { n.Id, n.DraftNumber, n.Stage })
            .ToListAsync(cancellationToken);
        var canonDraftId = (priorBookDrafts
                .Where(n => n.Stage == ProjectStage.Completed)
                .OrderByDescending(n => n.DraftNumber)
                .FirstOrDefault()
            ?? priorBookDrafts.OrderByDescending(n => n.DraftNumber).First()).Id;

        // The next book number in the series. A DB query won't see the just-backfilled book-one drafts still in
        // the change tracker, so a brand-new series reads no existing sequence and the sequel becomes book 2.
        var highestSequence = await db.Novels
            .Where(n => n.SeriesId == seriesId)
            .MaxAsync(n => (int?)n.SeriesSequence, cancellationToken) ?? 1;

        var text = await docs.FetchText(fileId, accessToken, cancellationToken);
        var now = clock.UtcNow;
        var book = new Novel
        {
            // A sequel is titled from its own manuscript, never the prior book's title.
            Title = string.IsNullOrWhiteSpace(title) ? ManuscriptTitle.FromFileName(fileName) : title,
            GoogleDocId = fileId,
            GoogleDocName = fileName,
            ArtStyle = string.IsNullOrWhiteSpace(artStyle) ? previous.ArtStyle : artStyle,
            SeriesId = seriesId,
            PreviousBookId = canonDraftId,
            SeriesSequence = highestSequence + 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        // A new book starts its own draft chain: the project is identified by this book's own id.
        book.ProjectId = book.Id;
        book.Source = new SourceText { NovelId = book.Id, FullText = text, CharacterCount = text.Length };
        book.WordCount = Words(text);

        db.Novels.Add(book);
        await db.SaveChangesAsync(cancellationToken);
        return book.Id;
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
        novel.RetryNotBeforeUtc = null;
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
        //
        // Completed is excluded for a different reason, and it is the one that costs money. This method
        // regresses novel.Stage by writing it DIRECTLY — it never calls ProjectStateMachine.NextStage — so
        // Completed being a terminal stage there protected nothing at all. A finished book whose rendered
        // file went missing (a reboot mid-flush, a storage sweep, an interrupted write) was pulled back
        // into the pipeline at the next app start and re-run, which on a full manuscript means the most
        // expensive stage billed a second time for a file the owner never touched.
        //
        // The accepted cost, measured rather than assumed: a Completed book whose report PDF is gone stays
        // Completed with no PDF on disk. What is NOT lost is the report — GetReportAsync rebuilds it from
        // the stored JSON on BookAnalysis, so the in-app reader still shows all of it, and an archived
        // book also has its Drive copy (which this method already skips). So the loss is one local file,
        // and regaining it is the owner's deliberate act rather than a side effect of opening the app.
        var novelIds = await db.Novels
            .Where(n => n.Stage != ProjectStage.Cancelled
                && n.Stage != ProjectStage.Failed
                && n.Stage != ProjectStage.Completed)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        var healed = 0;
        foreach (var id in novelIds)
            healed += await VerifyNovelOutputs(db, id, cancellationToken);

        if (healed > 0)
            await db.SaveChangesAsync(cancellationToken);
        return healed;
    }

    private async Task<int> VerifyNovelOutputs(FolioDbContext db, Guid novelId, CancellationToken cancellationToken)
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

        var project = novel.ProjectId;
        db.Novels.Remove(novel);
        await db.SaveChangesAsync(cancellationToken);

        // The DB row is gone; drop the output subtree too. Files failing to delete shouldn't resurrect
        // the (already-removed) project, so this runs after the row is committed.
        imageStore.Delete(id.ToString());

        // And if that was the book's last draft, the phrasings it taught the owner's voice leave with
        // it. Checked after the delete, so "is this book still in the library" is answered by what is
        // actually left rather than by what was there a moment ago.
        var remaining = await db.Novels.AnyAsync(n => n.ProjectId == project, cancellationToken);
        if (!remaining)
        {
            await DiscardVoiceOf(project, cancellationToken);
        }
    }

    /// <summary>
    /// The Work desk's line about one book, in the owner's voice (D96).
    ///
    /// <para><b>Composed when the book has moved, remembered when it has not.</b> The line comes from
    /// a model and the shelf is the screen the app opens on, painting again on every return to it, so
    /// composing on each ask would be a call per book per glance. <see cref="VoicedLinePlan"/> makes
    /// the cost one call per book per state change, which is the rate at which the line has anything
    /// new to say.</para>
    ///
    /// <para>The neutral <paramref name="fallback"/> is returned but never <i>stored</i>: remembering
    /// it would leave the desk holding today's wording as though it were the owner's, and never
    /// trying again once a voice had been learned.</para>
    /// </summary>
    public async Task<string> VoicedLineAsync(Guid id, string fallback, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null)
            return fallback;

        // The model is told what is true, in words, and a stopped book gets no line at all: the desk shows
        // the error where this would go, and a cheerful note over a stalled book is worse than silence.
        // (It used to be told only the stage's name, and wrote "Draft 1 is complete" over a book that was
        // part-way through its reads and had stopped on an error.)
        var status = await BuildStatus(db, id, rateLimits, clock.UtcNow, cancellationToken);
        var facts = status is null ? null : DeskStatus.VoiceFacts(status);
        if (facts is null)
            return fallback;

        if (!VoicedLinePlan.NeedsComposing(novel.VoicedLine, novel.VoicedLineStage, novel.Stage))
            return novel.VoicedLine;

        var composed = await ComposeLine(novel, facts, cancellationToken);
        if (string.IsNullOrWhiteSpace(composed))
            return fallback;

        novel.VoicedLine = composed;
        novel.VoicedLineStage = novel.Stage;
        await db.SaveChangesAsync(cancellationToken);
        return composed;
    }

    /// <summary>
    /// One line in the owner's voice about where this book has got to, or nothing at all — which is
    /// every ordinary case of the voice being unavailable: no key, no profile learned yet, offline.
    /// Never throws into a screen.
    /// </summary>
    private async Task<string?> ComposeLine(Novel novel, string facts, CancellationToken cancellationToken)
    {
        if (voiceStore is null || voiceClient is null)
            return null;

        try
        {
            var profile = await voiceStore.LoadAsync(cancellationToken);
            if (profile.IsEmpty)
                return null;

            var message = await voiceClient.ComposeAsync(
                profile,
                new MessageRequest(
                    VoicedOccasions.DeskLine,
                    $"Title: {novel.Title}. Draft {novel.DraftNumber}. {facts}",
                    MaxWords: 18),
                cancellationToken);

            return string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Takes one book's anchors out of the shared voice profile.
    ///
    /// <para>Best-effort and silent on failure: the delete is the owner's instruction and has already
    /// happened, so an unreachable Drive must not throw out of it or leave the book in the library.
    /// The worst case is a few stale phrasings in a profile the next ingestion rewrites anyway.</para>
    /// </summary>
    private async Task DiscardVoiceOf(Guid projectId, CancellationToken cancellationToken)
    {
        if (voiceStore is null)
            return;

        try
        {
            var profile = await voiceStore.LoadAsync(cancellationToken);
            var after = profile.Discard(projectId);
            if (!ReferenceEquals(after, profile) && after != profile)
            {
                await voiceStore.SaveAsync(after, cancellationToken);
            }
        }
        catch (Exception)
        {
            // Deliberately swallowed: see above. Nothing the owner can act on, and nothing worth
            // failing a delete over.
        }
    }

    /// <summary>
    /// Records a jacket the <b>owner</b> chose.
    /// </summary>
    public Task ReplaceCoverAsync(Guid id, byte[] jacket, CancellationToken cancellationToken) =>
        SetCoverAsync(id, jacket, owners: true, cancellationToken);

    /// <summary>
    /// Records a jacket the app <b>painted</b>.
    /// </summary>
    public Task SetPaintedCoverAsync(Guid id, byte[] jacket, CancellationToken cancellationToken) =>
        SetCoverAsync(id, jacket, owners: false, cancellationToken);

    /// <summary>
    /// Puts a previous jacket back — the undo behind a repaint, and the reason repainting asks
    /// nothing first. <paramref name="path"/> of null restores having had no jacket at all, which
    /// is where the first repaint came from.
    /// </summary>
    public async Task RestoreCoverAsync(Guid id, string? path, bool wasOwners, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null)
        {
            return;
        }

        novel.CoverPath = path;
        novel.CoverIsOwners = wasOwners;
        novel.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a jacket to a file of its own and points the book at it.
    ///
    /// <para>Never overwrites: the previous jacket stays on disk so the fold can restore it. They
    /// live under the book's own folder, so deleting the book takes them with it.</para>
    /// </summary>
    private async Task SetCoverAsync(Guid id, byte[] jacket, bool owners, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null)
        {
            return;
        }

        // The absolute path Save returns, the way every other rendered file is recorded here.
        novel.CoverPath = imageStore.Save(
            Path.Combine(id.ToString(), "covers", $"{Guid.NewGuid():N}.png"), jacket);
        novel.CoverIsOwners = owners;
        novel.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// What a series is called: the owner's name for it, or the title of its first book until they
    /// give it one. A standalone book has no series and no name.
    /// </summary>
    private static async Task<string?> SeriesNameOf(FolioDbContext db, Novel novel, CancellationToken cancellationToken)
    {
        if (novel.SeriesId is not { } series)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(novel.SeriesName))
        {
            return novel.SeriesName;
        }

        return await db.Novels
            .Where(other => other.SeriesId == series)
            .OrderBy(other => other.SeriesSequence)
            .Select(other => other.Title)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Renames a series, across every draft of every book in it — the name is denormalised the way
    /// the rest of the series fields are, so a rename is a write to all of them.
    ///
    /// <para>A blank name clears it rather than storing whitespace, which puts the series back to
    /// being called after its first book. That is the same act as never having named it, so it
    /// should leave the same state behind.</para>
    /// </summary>
    public async Task RenameSeriesAsync(Guid seriesId, string? name, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var books = await db.Novels.Where(novel => novel.SeriesId == seriesId).ToListAsync(cancellationToken);
        if (books.Count == 0)
        {
            return;
        }

        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        foreach (var book in books)
        {
            book.SeriesName = trimmed;
            book.UpdatedAtUtc = clock.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>What is on the <b>shelf</b>: what the owner has put away.</summary>
    public async Task<IReadOnlyList<NovelStatus>> ListShelvedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Novels
            .Where(n => n.ShelvedAtUtc != null && n.DeletedAtUtc == null)
            .OrderByDescending(n => n.ShelvedAtUtc)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

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

    /// <summary>
    /// Puts a finished book away.
    ///
    /// <para><b>Only a finished one.</b> The shelf is what has been dealt with; a book still being
    /// read has not been, and shelving it would hide work in progress behind a spine. Asked of an
    /// unfinished book this does nothing rather than failing — the action is not offered for one, so
    /// arriving here means something raced, and a book left on the desk is the harmless outcome.</para>
    /// </summary>
    public async Task ShelveAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null || novel.Stage != ProjectStage.Completed || novel.ShelvedAtUtc is not null)
            return;

        novel.ShelvedAtUtc = clock.UtcNow;
        novel.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Takes it back off. This is the undo behind the dog-ear (§6), which is why shelving asks
    /// nothing first: the book comes straight back, with no trace that it went.
    /// </summary>
    public async Task UnshelveAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null || novel.ShelvedAtUtc is null)
            return;

        novel.ShelvedAtUtc = null;
        novel.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Marks a book deleted — undo-first (§6). This is not <see cref="DeleteAsync"/>: nothing on
    /// disk or in the row itself is touched, so it hides at once from <see cref="ListAsync"/> and
    /// <see cref="ListShelvedAsync"/> and <see cref="RestoreAsync"/> can put it back exactly as it
    /// was. The caller is responsible for the real delete once its own undo window has closed
    /// without being turned; this method only marks the moment the owner asked.
    /// </summary>
    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null || novel.DeletedAtUtc is not null)
            return;

        novel.DeletedAtUtc = clock.UtcNow;
        novel.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Takes a soft delete back. The undo behind the dog-ear for <see cref="SoftDeleteAsync"/> — the
    /// book was never actually touched, so this just clears the mark.
    /// </summary>
    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var novel = await db.Novels.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (novel is null || novel.DeletedAtUtc is null)
            return;

        novel.DeletedAtUtc = null;
        novel.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// How long a manuscript is. Whitespace-separated runs, which is what the report counts too —
    /// two different answers to "how long is this book" on the same screen is worse than either.
    /// </summary>
    internal static int Words(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// What is on the <b>desk</b>: everything the owner has not put away yet. A finished review
    /// stays here until they shelve it — see <see cref="ShelveAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<NovelStatus>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Novels
            .Where(n => n.ShelvedAtUtc == null && n.DeletedAtUtc == null)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
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

    /// <summary>
    /// A whole book, read from any one of its drafts: every draft in the project, the readiness of
    /// each draft that has been reported on, and whether there is a report to open.
    ///
    /// <para>The readiness list is deliberately shorter than the draft list when a draft is still
    /// being read. Counting a reportless draft as four zeroes would draw its Must line arriving at
    /// the axis, which says the problems were fixed rather than not yet found — the one reading of
    /// that chart that would be actively untrue.</para>
    /// </summary>
    public async Task<BookRecord?> GetBookAsync(Guid draftId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var projectId = await db.Novels
            .Where(n => n.Id == draftId)
            .Select(n => (Guid?)n.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
        if (projectId is null)
            return null;

        var drafts = await db.Novels
            .Where(n => n.ProjectId == projectId)
            .OrderBy(n => n.DraftNumber)
            .Select(n => new
            {
                n.Id,
                n.DraftNumber,
                n.CreatedAtUtc,
                n.WordCount,
                n.Title,
                n.CoverPath,
                n.Stage,
                n.SeriesName,
                n.SeriesId,
                n.SeriesSequence,
            })
            .ToListAsync(cancellationToken);
        if (drafts.Count == 0)
            return null;

        var latest = drafts[^1];

        var reports = await db.BookAnalyses
            .Where(a => drafts.Select(d => d.Id).Contains(a.NovelId))
            .Select(a => new { a.NovelId, a.ConsolidatedJson, a.ExpansionJson, a.PdfStatus, a.PdfPath })
            .ToListAsync(cancellationToken);

        var readiness = new List<DraftFindings>();
        var genre = string.Empty;
        var length = LengthAssessment.Empty;
        foreach (var draft in drafts)
        {
            var stored = reports.FirstOrDefault(report => report.NovelId == draft.Id);
            if (stored?.ConsolidatedJson is null)
                continue;

            BookAnalysisResult? report;
            try
            {
                report = JsonSerializer.Deserialize<BookAnalysisResult>(
                    stored.ConsolidatedJson, JsonResponse.CaseInsensitive);
            }
            catch (JsonException)
            {
                // A report stored in a shape this version cannot read is a draft with no counts, not
                // a book that cannot be opened. The chart simply skips it, as it skips an unread draft.
                continue;
            }

            if (report is null)
                continue;

            readiness.Add(DraftReadiness.From(draft.DraftNumber, report));
            if (draft.Id == latest.Id)
            {
                genre = report.Genre;
                length = Length(stored.ExpansionJson, report);
            }
        }

        var latestReport = reports.FirstOrDefault(report => report.NovelId == latest.Id);

        // Books, not drafts: a series of one trilogy-in-progress holds one book however many times
        // its author has revised it.
        var seriesLength = latest.SeriesId is null
            ? 0
            : await db.Novels
                .Where(n => n.SeriesId == latest.SeriesId)
                .Select(n => n.ProjectId)
                .Distinct()
                .CountAsync(cancellationToken);

        return new BookRecord(
            projectId.Value,
            latest.Id,
            latest.Title,
            latest.CoverPath,
            latest.Stage.ToString(),
            latest.SeriesName,
            latest.SeriesSequence,
            latest.SeriesId is not null,
            seriesLength,
            [.. drafts.Select(draft =>
                new BookDraft(draft.Id, draft.DraftNumber, draft.CreatedAtUtc, draft.WordCount))],
            readiness,
            latestReport is { PdfStatus: WorkStatus.Completed, PdfPath: not null },
            genre,
            length);
    }

    /// <summary>
    /// The length read, preferring the expansion pass's own measured range. Falling back to the
    /// consolidated half's count with no range is deliberate: a count with no norm behind it says
    /// how long the book is and claims nothing about whether that is right, which is the honest
    /// thing to print before the read that would know has run.
    /// </summary>
    private static LengthAssessment Length(string? expansionJson, BookAnalysisResult report)
    {
        if (expansionJson is not null)
        {
            try
            {
                var expansion = JsonSerializer.Deserialize<ExpansionReport>(
                    expansionJson, JsonResponse.CaseInsensitive);
                if (expansion?.Length is { WordCount: > 0 } measured)
                    return measured;
            }
            catch (JsonException)
            {
                // An unreadable expansion read costs the norm, not the page.
            }
        }

        return report.WordCount > 0
            ? new LengthAssessment(report.WordCount, 0, 0, string.Empty)
            : LengthAssessment.Empty;
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
            .Select(a => new { a.ConsolidatedJson, a.CraftJson, a.ExpansionJson, a.SeriesContinuityJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (stored?.ConsolidatedJson is null)
            return null;

        var report = JsonSerializer.Deserialize<BookAnalysisResult>(stored.ConsolidatedJson, JsonResponse.CaseInsensitive)
                     ?? BookAnalysisResult.Empty;
        var craft = stored.CraftJson is null
            ? CraftReport.Empty
            : JsonSerializer.Deserialize<CraftReport>(stored.CraftJson, JsonResponse.CaseInsensitive) ?? CraftReport.Empty;
        var expansion = stored.ExpansionJson is null
            ? ExpansionReport.Empty
            : JsonSerializer.Deserialize<ExpansionReport>(stored.ExpansionJson, JsonResponse.CaseInsensitive) ?? ExpansionReport.Empty;
        var seriesContinuity = stored.SeriesContinuityJson is null
            ? SeriesContinuity.Empty
            : JsonSerializer.Deserialize<SeriesContinuity>(stored.SeriesContinuityJson, JsonResponse.CaseInsensitive) ?? SeriesContinuity.Empty;

        return report.WithCraft(craft).WithExpansion(expansion).WithSeriesContinuity(seriesContinuity);
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

        var currentFindings = await db.Findings
            .Where(f => f.NovelId == draftId)
            .Select(f => new { f.ThreadId, f.Category, f.Chapter, f.Detail, f.Status })
            .ToListAsync(cancellationToken);

        // Status is assigned by the reconciliation against the whole thread history: New = first appearance,
        // Regressed = a previously-fixed issue returned. Resolved = a thread the previous draft still carried
        // that no current finding claims (matched by identity, never by text).
        var newFindings = currentFindings
            .Where(f => f.Status == FindingStatus.New)
            .Select(f => new DiffFinding(FindingChange.New, f.Category, f.Chapter, f.Detail))
            .ToList();
        var regressedFindings = currentFindings
            .Where(f => f.Status == FindingStatus.Regressed)
            .Select(f => new DiffFinding(FindingChange.Regressed, f.Category, f.Chapter, f.Detail))
            .ToList();
        var currentThreadIds = currentFindings.Select(f => f.ThreadId).ToHashSet();
        var resolvedFindings = (await db.Findings
                .Where(f => f.NovelId == previousId)
                .Select(f => new { f.ThreadId, f.Category, f.Chapter, f.Detail })
                .ToListAsync(cancellationToken))
            .Where(f => !currentThreadIds.Contains(f.ThreadId))
            .Select(f => new DiffFinding(FindingChange.Resolved, f.Category, f.Chapter, f.Detail))
            .ToList();

        var changedScenes = await LoadChangedScenePairsAsync(db, draftId, cancellationToken);

        return new DraftDiff(draftId, previousId, resolvedFindings, newFindings, regressedFindings, changedScenes);
    }

    // The before/after evidence images for every scene that changed between this draft and the one before,
    // in reading order. A scene is paired to its predecessor by PreviousSceneId (set at seed time by the
    // cross-draft SceneMatcher); either image may be null when that draft's render was unavailable.
    private async Task<IReadOnlyList<DiffScenePair>> LoadChangedScenePairsAsync(
        FolioDbContext db, Guid draftId, CancellationToken cancellationToken)
    {
        var changed = await db.Scenes
            .Where(s => s.NovelId == draftId && s.Changed && s.PreviousSceneId != null)
            .OrderBy(s => s.Number)
            .Select(s => new { s.Number, s.ChapterLabel, s.ImagePath, s.PreviousSceneId })
            .ToListAsync(cancellationToken);
        if (changed.Count == 0)
            return [];

        var previousIds = changed.Select(s => s.PreviousSceneId!.Value).ToList();
        var previousPaths = await db.Scenes
            .Where(s => previousIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ImagePath })
            .ToDictionaryAsync(s => s.Id, s => s.ImagePath, cancellationToken);

        byte[]? Read(string? path) =>
            path is not null && imageStore.Exists(path) ? imageStore.Read(path) : null;

        return changed
            .Select(s =>
            {
                var citation = string.IsNullOrWhiteSpace(s.ChapterLabel) ? $"Scene {s.Number}" : $"{s.ChapterLabel} · Scene {s.Number}";
                previousPaths.TryGetValue(s.PreviousSceneId!.Value, out var priorPath);
                return new DiffScenePair(citation, Read(priorPath), Read(s.ImagePath));
            })
            .ToList();
    }

    private static async Task<NovelStatus?> BuildStatus(
        FolioDbContext db, Guid id, RateLimitOptions limits, DateTimeOffset now, CancellationToken cancellationToken)
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

        var (reads, step) = await ReportPosition(db, novel, analysisNotes, analysisReady, cancellationToken);
        var deliveryNote = await db.BookAnalyses
            .Where(a => a.NovelId == id)
            .Select(a => a.RemarkableNote)
            .FirstOrDefaultAsync(cancellationToken);

        return new NovelStatus(
            novel.Id, novel.Title, novel.Stage.ToString(), novel.LastError,
            chunks, sheets, sceneImages, analysisNotes, analysis, analysisReady, needsAttention,
            estimate, rateLimitedUntil,
            novel.DraftNumber, novel.PreviousDraftId is not null,
            novel.SeriesId is not null, novel.SeriesSequence, novel.SeriesId,
            await SeriesNameOf(db, novel, cancellationToken),
            novel.ShelvedAtUtc is not null, novel.WordCount,
            novel.CoverPath, novel.CoverIsOwners,
            reads, step, novel.StageAttempts, limits.MaxAttempts, deliveryNote);
    }

    // Where the book is inside the revision report: how many of the whole-book reads are done and which
    // part of the report is being worked on. Only meaningful once the chapters have been mapped; before
    // that the desk reports the mapping's own progress.
    private static async Task<(PhaseProgress? Reads, string? Step)> ReportPosition(
        FolioDbContext db, Novel novel, PhaseProgress analysisNotes, bool rendered, CancellationToken cancellationToken)
    {
        var mapped = analysisNotes.Total > 0 && analysisNotes.Done >= analysisNotes.Total;
        if (novel.Stage != ProjectStage.ComposingAnalysis || !mapped)
            return (null, null);

        var book = await db.BookAnalyses.FirstOrDefaultAsync(a => a.NovelId == novel.Id, cancellationToken);
        var finished = await db.PassReads
            .Where(pass => pass.NovelId == novel.Id && pass.Json != null)
            .Select(pass => pass.PassId)
            .ToListAsync(cancellationToken);

        var passes = ReadingPlan.Passes;
        var reads = new PhaseProgress(passes.Count(pass => finished.Contains(pass.Id)), passes.Count);
        var next = passes.FirstOrDefault(pass => !finished.Contains(pass.Id));

        var step = ReportStep.Describe(new ReportProgress(
            StoryReport: book?.ConsolidatedJson is not null,
            Craft: book?.CraftJson is not null,
            Voice: book?.VoiceJson is not null,
            Findings: book?.FindingsReconciled == true,
            Expansion: book?.ExpansionJson is not null,
            SeriesPending: novel.PreviousBookId is not null && book?.SeriesContinuityJson is null,
            reads.Done, reads.Total, next?.Name,
            JacketPending: novel.CoverPath is null && !novel.CoverAttempted,
            Rendered: rendered));
        return (reads, step);
    }

    // A human-readable snapshot of everything that might be wrong: config, last worker run, and per
    // project the stage plus each phase's done/failed/retry counts and a sample of the error messages
    // on the stuck units. Surfaces "sheets 0 of 124, 124 failed: <reason>" without reading the raw log.
    public async Task<string> BuildDiagnosticsReportAsync(DiagnosticsContext context, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var report = new StringBuilder();

        report.AppendLine($"Folio diagnostics — {clock.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
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

            AppendReport(report, novel, await db.BookAnalyses
                .FirstOrDefaultAsync(a => a.NovelId == novel.Id, cancellationToken));
        }

        return report.ToString();
    }

    /// <summary>
    /// Where the document is, and whether it went anywhere.
    ///
    /// <para>This is the question an owner asks once a review has FINISHED, and it was the one thing
    /// the export could not answer. Every other line here is about a review that will not finish; a
    /// finished book whose delivery quietly gave up looks exactly like one whose delivery worked,
    /// because the stage reads Completed either way and the give-up is a log line on a phone nobody
    /// can read. Delivery is best-effort by design — neither target may wedge a review — which is
    /// precisely why its outcome has to be legible somewhere.</para>
    /// </summary>
    private static void AppendReport(StringBuilder report, Novel novel, BookAnalysis? analysis)
    {
        if (analysis is null || analysis.PdfStatus == WorkStatus.Pending)
        {
            report.AppendLine("  Report: not rendered yet");
            return;
        }

        if (analysis.PdfStatus != WorkStatus.Completed)
        {
            report.AppendLine($"  Report: could not be rendered ({analysis.PdfStatus})");
            return;
        }

        var where = new List<string>();
        if (analysis.ArchivedDriveFileId is not null)
            where.Add($"Drive [{analysis.ArchivedDriveFileId}]");
        if (analysis.DeliveredToRemarkable)
            where.Add("reMarkable (settled — delivered, or given up)");
        if (analysis.PdfPath is not null)
            where.Add($"on this device at {analysis.PdfPath}");

        report.AppendLine($"  Report: rendered as \"{DeliveredDocument.FileName(novel.Title, novel.DraftNumber)}\"");
        report.AppendLine(where.Count == 0
            ? "      • nowhere — rendered, and the file is gone"
            : $"      • {string.Join("; ", where)}");

        // "on this device only" is a state worth naming rather than inferring from an absence: it
        // means the owner can still open it from the book page, and that nothing else has a copy.
        if (analysis.ArchivedDriveFileId is null && !analysis.DeliveredToRemarkable && analysis.PdfPath is not null)
            report.AppendLine("      • on this device only — nothing has been delivered");
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
