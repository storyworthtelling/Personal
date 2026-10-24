using System.Net;
using Constitution.Domain.Delivery;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Notices;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Waiting;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.Ink;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// C-2.2.3 — the morning read-back worker's service (D87, R2, R3, R4). Every outside thing is a fake: the tablet's
/// cloud, the vision read, the stores and the notice board. What is held here is the order things happen in and what
/// each failure leaves behind, because the rule that matters most is invisible when it is broken: a page trashed
/// before its answer is safe is a workout the plan can never learn about.
/// </summary>
public class ReadBackServiceTests
{
    private static string Name => Workout().Name;

    private static Rig Arranged(bool proven = false, Func<ExpectedDocument, InkDocumentRead>? reply = null, params ExpectedDocument[] documents)
    {
        var rig = new Rig(proven, documents.Length == 0 ? [Workout()] : documents);
        rig.Vision.Reply = reply ?? (document => Read(document, PenStrokes.Seen, (2, "done")));
        return rig;
    }

    /// <summary>Each named call happened, and in this order (other calls may sit between).</summary>
    private static void InOrder(CallLog calls, params string[] expected)
    {
        var at = -1;
        foreach (var call in expected)
        {
            var next = calls.Calls.IndexOf(call, at + 1);
            Assert.True(next > at, $"Expected '{call}' after position {at} in: {string.Join(", ", calls.Calls)}");
            at = next;
        }
    }

    // ---- C-2.2.3 acceptance: trashes only after saving ----

    [Fact]
    public async Task TrashesOnlyAfterSaving()
    {
        var rig = Arranged(reply: document => WithDoubt(document, 2, "done", 0.5, marked: true));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        InOrder(rig.Calls, "Download", "Read", "SaveResult", "MarkSaved", "Board", "Trash", "Retire");
        Assert.Single(rig.Calls.Calls, "Trash");
        Assert.Equal([$"id-{Name}"], rig.Tablet.Trashed);
        Assert.Equal([Name], rig.Expected.Retired);
    }

    [Fact]
    public async Task TheLedgerIsSavedBeforeThePageGoes()
    {
        var rig = Arranged();

        await rig.RunOne();

        InOrder(rig.Calls, "Download", "Read", "SaveResult", "SaveLedger", "MarkSaved", "Trash", "Retire");
    }

    [Fact]
    public async Task TheVisionIsHandedTheCloudsPdfAndTheDocument()
    {
        var rig = Arranged();

        await rig.RunOne();

        Assert.Equal(rig.Tablet.Pdf, rig.Vision.LastPdf);
        Assert.Equal(Name, rig.Vision.LastDocument!.Name);
        Assert.Equal([Name], rig.Tablet.Downloaded);
    }

    // ---- what a read means for the ledger ----

    [Fact]
    public async Task ATickSettlesTheDayAsDone()
    {
        var rig = Arranged();

        var outcome = await rig.RunOne();

        var settled = Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(2, settled.PagesRead);
        Assert.Equal(0, settled.Questions);
        Assert.Equal(SessionOutcome.Done, rig.Ledger.Held.OutcomeOf(Person, Day));
        Assert.Equal(SessionKind.Strength, Assert.Single(rig.Ledger.Held.Records).Kind);
    }

    [Fact]
    public async Task ATickOnOnlyThePageAfterTheFirstIsStillDone()
    {
        // Both pages carry a box called "done"; the second page's tick must not be lost to the first page's silence.
        var rig = Arranged(reply: document => Read(document, PenStrokes.Seen, (3, "done")));

        await rig.RunOne();

        Assert.Equal(SessionOutcome.Done, rig.Ledger.Held.OutcomeOf(Person, Day));
    }

    [Fact]
    public async Task ACircledWordWithNoTickMakesThePageDone()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.Seen, (2, "feel-2")));

        await rig.RunOne();

        Assert.Equal(SessionOutcome.Done, rig.Ledger.Held.OutcomeOf(Person, Day));
        var saved = rig.Results.Load(Name)!;
        Assert.Contains(saved.All, resolution => resolution is InkResolution.PageDone { Rating: "right" });
    }

    [Fact]
    public async Task EveryBoxEmptyOnceInkIsProvenIsASkip()
    {
        var rig = Arranged(proven: true, reply: document => Read(document, PenStrokes.None));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(SessionOutcome.Skipped, rig.Ledger.Held.OutcomeOf(Person, Day));
        Assert.Equal([$"id-{Name}"], rig.Tablet.Trashed);
    }

    [Fact]
    public async Task APageNotReadIsNeverASkip()
    {
        var rig = Arranged(proven: true, reply: NothingRead);

        var outcome = await rig.RunOne();

        var unfinished = Assert.IsType<DocumentReadBack.Unfinished>(outcome);
        Assert.NotEmpty(unfinished.Why);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Empty(rig.Ledger.Held.Records);
        Assert.Equal(0, rig.Results.Saves);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Retired);
        Assert.Empty(rig.Notices.Board.Waiting());
    }

    [Fact]
    public async Task PagesNotReadAreNotAMissingRoundTrip()
    {
        // Nothing was read, so nothing can be said about whether ink comes through: no notice about it, only a page kept.
        var rig = Arranged(proven: false, reply: NothingRead);

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Unfinished>(outcome);
        Assert.Empty(rig.Notices.Board.Waiting());
        Assert.Equal(DiagnosticsOutcome.Partial, new ReadBackReport([outcome]).Outcome);
    }

    [Fact]
    public async Task OnePageReadAndOneNotKeepsEverythingBack()
    {
        var rig = Arranged(proven: true, reply: document =>
        {
            var read = Read(document, PenStrokes.Seen, (2, "done"));
            return new InkDocumentRead(new Dictionary<int, InkPageRead> { [2] = read.PageFor(2), [3] = InkPageRead.NotRead("lost") }, PenStrokes.Seen);
        });

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Unfinished>(outcome);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Equal(0, rig.Results.Saves);
        Assert.Empty(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task ABoxLeftOutOfTheReplyIsNotAnEmptyBox()
    {
        var rig = Arranged(proven: true, reply: document =>
        {
            var pages = document.Pages.ToDictionary(
                page => page.Number,
                page => InkPageRead.Read(EmptyBoxes(page).Where(read => !(page.Number == 3 && read.TargetId == "done"))));
            return new InkDocumentRead(pages, PenStrokes.None);
        });

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Unfinished>(outcome);
        Assert.Empty(rig.Ledger.Held.Records);
        Assert.Empty(rig.Tablet.Trashed);
    }

    // ---- low confidence goes to the Waiting page ----

    [Fact]
    public async Task LowConfidenceGoesToWaiting()
    {
        var rig = Arranged(reply: document => WithDoubt(document, 2, "done", 0.5, marked: true));

        var outcome = await rig.RunOne();

        var settled = Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(1, settled.Questions);
        var notice = Assert.Single(rig.Notices.Board.Waiting());
        Assert.Equal(NoticeKind.LowConfidenceRead, notice.Kind);
        Assert.Equal(LowConfidenceCause.Key("Goblet squat", "Ticked", "Not ticked"), notice.CauseKey);
        var row = Assert.Single(WaitingPageModel.From(rig.Notices.Board, Now(rig)).Items);
        Assert.Equal(["Ticked", "Not ticked"], row.Chips.Select(chip => chip.Value));
        Assert.Empty(rig.Ledger.Held.Records);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Equal([$"id-{Name}"], rig.Tablet.Trashed);
    }

    [Fact]
    public async Task TheQuestionIsSavedWithTheResultBeforeThePageGoes()
    {
        var rig = Arranged(reply: document => WithDoubt(document, 2, "done", 0.5, marked: true));

        await rig.RunOne();

        var saved = rig.Results.Load(Name)!;
        var question = Assert.Single(saved.Questions);
        Assert.Equal("done", question.SubjectId);
        Assert.Equal(2, Assert.Single(saved.Pages, page => page.Resolutions.Contains(question)).Number);
        InOrder(rig.Calls, "SaveResult", "Board", "Trash");
    }

    [Fact]
    public async Task TwoQuestionsOnTwoPagesAreBothAsked()
    {
        var rig = Arranged(reply: document =>
        {
            var first = WithDoubt(document, 2, "done", 0.5, marked: true);
            var second = WithDoubt(document, 3, "done", 0.4, marked: true);
            return new InkDocumentRead(
                new Dictionary<int, InkPageRead> { [2] = first.PageFor(2), [3] = second.PageFor(3) }, PenStrokes.Seen);
        });

        var settled = Assert.IsType<DocumentReadBack.Settled>(await rig.RunOne());

        Assert.Equal(2, settled.Questions);
        Assert.Equal(2, rig.Notices.Board.Waiting().Count);
    }

    [Fact]
    public async Task ASavedResultHoldsEveryPagesAnswers()
    {
        var rig = Arranged();

        await rig.RunOne();

        var saved = rig.Results.Load(Name)!;
        Assert.Equal(Name, saved.Name);
        Assert.Equal(DeliveryKind.Workout, saved.Kind);
        Assert.Equal(Day, saved.Day);
        Assert.Equal(Person, saved.Person);
        Assert.Equal(rig.Now, saved.ReadAt);
        Assert.Equal([2, 3], saved.Pages.Select(page => page.Number));
        Assert.Contains(saved.Pages[0].Resolutions, resolution => resolution is InkResolution.Ticked { SubjectId: "done" });
        Assert.Contains(saved.Pages[1].Resolutions, resolution => resolution is InkResolution.Skipped { SubjectId: "done" });
    }

    // ---- R3: the cloud's PDF carries no ink ----

    [Fact]
    public async Task MissingAnnotationsAreReportedNotSilent()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.None));

        var report = await rig.Service.Run();

        Assert.IsType<DocumentReadBack.NoMarksInCloud>(Assert.Single(report.Documents));
        Assert.Equal(DiagnosticsOutcome.FoundNothingRead, report.Outcome);
        Assert.Equal(["ink read back document found, nothing read, page kept"], rig.Log.Phrases);
    }

    [Fact]
    public async Task NoMarksInTheCloudRaisesANoticeAndKeepsThePage()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.None));

        await rig.RunOne();

        var notice = Assert.Single(rig.Notices.Board.Waiting());
        Assert.Equal(NoticeKind.NoPenMarks, notice.Kind);
        Assert.Equal(ReadBackNotices.NoPenMarksCause, notice.CauseKey);
        Assert.Equal(rig.Now, notice.CreatedAt);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Retired);
        Assert.Empty(rig.Expected.Saved);
    }

    [Fact]
    public async Task NoMarksInTheCloudRecordsNothingAboutTheDay()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.None));

        await rig.RunOne();

        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Equal(0, rig.Results.Saves);
        Assert.False(rig.Proof.PenMarksSeen);
    }

    [Fact]
    public async Task ARepeatedMorningDoesNotRaiseItTwice()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.None));

        await rig.RunOne();
        await rig.RunOne();

        Assert.Single(rig.Notices.Board.Waiting());
    }

    [Fact]
    public async Task ReadsThatCannotTellAreTreatedAsNoProofToo()
    {
        // The read neither saw strokes nor said it saw none: with nothing marked and nothing ever proven, a skip is not safe to write.
        var rig = Arranged(reply: document => Read(document, PenStrokes.Unknown));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.NoMarksInCloud>(outcome);
        Assert.Equal(0, rig.Ledger.Saves);
    }

    [Fact]
    public async Task AMarkProvesTheCloudPassesInkAndTakesTheNoticeAway()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.None));
        await rig.RunOne();
        Assert.Single(rig.Notices.Board.Waiting());

        rig.Vision.Reply = document => Read(document, PenStrokes.Seen, (2, "done"));
        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Empty(rig.Notices.Board.Waiting());
        Assert.Single(rig.Proof.Recorded);
    }

    [Fact]
    public async Task OnceProvenAnEmptyPageIsTheOwnersSilence()
    {
        var rig = Arranged(proven: true, reply: document => Read(document, PenStrokes.None));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Empty(rig.Notices.Board.Waiting());
        Assert.Equal(SessionOutcome.Skipped, rig.Ledger.Held.OutcomeOf(Person, Day));
    }

    // Review finding S4 · ink is proven by two documents on two mornings, not by one read's word.

    private static readonly DateOnly TheMorning = DateOnly.FromDateTime(Morning.DateTime);

    [Fact]
    public async Task OneDocumentWithInkDoesNotProveTheCloudCarriesIt()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.Seen, (2, "done")));

        var outcome = await rig.RunOne();

        // The page's own marks settle that page, and the sighting is kept; the flag that makes later blanks skips is not set.
        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.False(rig.Proof.PenMarksSeen);
        Assert.Equal([new InkSighting(Name, TheMorning)], rig.Proof.Recorded.ToArray());
        Assert.Equal(SessionOutcome.Done, rig.Ledger.Held.OutcomeOf(Person, Day));
    }

    [Fact]
    public async Task AConfidentMarkAloneIsASighting()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.Unknown, (2, "done")));

        await rig.RunOne();

        Assert.Single(rig.Proof.Recorded);
        Assert.False(rig.Proof.PenMarksSeen);
    }

    [Fact]
    public async Task TwoDocumentsOnTwoMorningsProveTheInk()
    {
        var next = Workout(Day.AddDays(1));
        var rig = Arranged(documents: [Workout(), next], reply: document => Read(document, PenStrokes.Seen, (2, "done")));

        await rig.Service.Run();
        Assert.False(rig.Proof.PenMarksSeen);
        rig.Now = Morning.AddDays(1);
        await rig.Service.Run();

        Assert.True(rig.Proof.PenMarksSeen);
        Assert.Equal(TheMorning.AddDays(1), rig.Proof.Current.EstablishedOn);
    }

    [Fact]
    public async Task ABlankPageIsKeptTheMorningTheInkIsProven()
    {
        // Proof was established this morning, so a page that is blank now may be one whose ink the cloud has not caught up with.
        var rig = Arranged(reply: document => Read(document, PenStrokes.None));
        rig.Proof.Current = InkProof.EstablishedOnMorning(TheMorning);

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Unfinished>(outcome);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Equal(0, rig.Results.Saves);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Retired);
        Assert.Empty(rig.Notices.Board.Waiting());
    }

    [Fact]
    public async Task TheBlankPageIsASkipTheMorningAfter()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.None));
        rig.Proof.Current = InkProof.EstablishedOnMorning(TheMorning);
        await rig.RunOne();

        rig.Now = Morning.AddDays(1);
        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(SessionOutcome.Skipped, rig.Ledger.Held.OutcomeOf(Person, Day));
        Assert.Single(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task ABlankPageAfterTheProofWasEstablishedEarlierIsASkip()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.None));
        rig.Proof.Current = InkProof.EstablishedOnMorning(TheMorning.AddDays(-1));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(SessionOutcome.Skipped, rig.Ledger.Held.OutcomeOf(Person, Day));
    }

    [Fact]
    public async Task APageWithAMarkIsNotHeldOnTheMorningTheInkIsProven()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.Seen, (2, "done")));
        rig.Proof.Current = InkProof.EstablishedOnMorning(TheMorning);

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(SessionOutcome.Done, rig.Ledger.Held.OutcomeOf(Person, Day));
    }

    [Fact]
    public async Task ABlankPageReadRightAfterTheProofIsEstablishedIsHeldToo()
    {
        // Two documents in one morning: the first establishes the proof (a sighting on an earlier morning was already kept),
        // and the second is blank. It is read after the proof exists, so it waits a morning like any other blank page.
        var first = Workout(Day, "A");
        var second = Workout(Day, "B");
        var rig = Arranged(documents: [first, second], reply: document => document.Subject == "A"
            ? Read(document, PenStrokes.Seen, (2, "done"))
            : Read(document, PenStrokes.None));
        rig.Proof.Current = InkProof.None.Record(new InkSighting("Constitution — Workout — 2026-11-02", TheMorning.AddDays(-1)));

        var report = await rig.Service.Run();

        Assert.IsType<DocumentReadBack.Settled>(report.Documents[0]);
        Assert.True(rig.Proof.PenMarksSeen);
        Assert.IsType<DocumentReadBack.Unfinished>(report.Documents[1]);
        Assert.Single(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task StrokesWithNoBoxMarkedAreNotASkipEvenWhenProven()
    {
        // The owner wrote in the notes margin and ticked nothing. The read saw ink, so a blank box may be a tick it did not
        // attribute; it is not safe to call the session skipped, and the page stays.
        var rig = Arranged(proven: true, reply: document => Read(document, PenStrokes.Seen));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Unfinished>(outcome);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Equal(0, rig.Results.Saves);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Retired);
    }

    [Fact]
    public async Task StrokesWithNoBoxMarkedAreNotASkipBeforeProofEither()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.Seen));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Unfinished>(outcome);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Empty(rig.Proof.Recorded);
        Assert.False(rig.Proof.PenMarksSeen);
        Assert.Empty(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task StrokesAloneAreNotASighting()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.Seen));

        await rig.RunOne();

        Assert.Empty(rig.Proof.Recorded);
    }

    [Fact]
    public async Task APageHeldForItsStrokesIsReadAgainNextMorning()
    {
        var rig = Arranged(reply: document => Read(document, PenStrokes.Seen));
        await rig.RunOne();

        rig.Vision.Reply = document => Read(document, PenStrokes.Seen, (2, "done"));
        rig.Now = Morning.AddDays(1);
        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(SessionOutcome.Done, rig.Ledger.Held.OutcomeOf(Person, Day));
    }

    [Fact]
    public async Task ADoubtfulMarkIsNeitherProofNorNone()
    {
        var rig = Arranged(reply: document => WithDoubt(document, 2, "done", 0.5, marked: true));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.False(rig.Proof.PenMarksSeen);
        Assert.Empty(rig.Proof.Recorded);
        Assert.DoesNotContain(rig.Notices.Board.Waiting(), notice => notice.Kind == NoticeKind.NoPenMarks);
    }

    // ---- a failure never loses the page ----

    private static ExpectedDocument BrokenWorkout()
    {
        var page = Workout().Pages[0];
        return new ExpectedDocument(
            DeliveryKind.Workout, Day, null, Person, SessionKind.Strength,
            [new ExpectedPage(2, page.Readout, [new ExercisePageRule("Goblet squat", "no-such-box", "feel")])]);
    }

    public static TheoryData<ReadBackStage> EveryStage => new()
    {
        ReadBackStage.Download, ReadBackStage.Read, ReadBackStage.Resolve,
        ReadBackStage.Persist, ReadBackStage.Ask, ReadBackStage.Trash,
    };

    [Theory]
    [MemberData(nameof(EveryStage))]
    public async Task AFailureAtAnyStageKeepsThePage(ReadBackStage stage)
    {
        var document = stage == ReadBackStage.Resolve ? BrokenWorkout() : Workout();
        var rig = new Rig(false, document)
        {
            Now = Morning,
        };
        rig.Vision.Reply = stage == ReadBackStage.Resolve
            ? read => Read(read, PenStrokes.Seen, (2, "feel-1"))
            : read => WithDoubt(read, 2, "done", 0.5, marked: true);
        var injected = new HttpRequestException("injected for " + stage);
        switch (stage)
        {
            case ReadBackStage.Download: rig.Tablet.DownloadFails = injected; break;
            case ReadBackStage.Read: rig.Vision.Fails = injected; break;
            case ReadBackStage.Persist: rig.Results.SaveFails = injected; break;
            case ReadBackStage.Ask: rig.Notices.Fails = injected; break;
            case ReadBackStage.Trash: rig.Tablet.TrashFails = injected; break;
        }

        var outcome = await rig.RunOne();

        var failed = Assert.IsType<DocumentReadBack.Failed>(outcome);
        Assert.Equal(stage, failed.Stage);
        if (stage != ReadBackStage.Resolve)
        {
            Assert.Same(injected, failed.Failure);
        }
        else
        {
            Assert.IsType<KeyNotFoundException>(failed.Failure);
        }

        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Retired);
        Assert.Equal(stage is ReadBackStage.Ask or ReadBackStage.Trash, rig.Expected.Saved.Contains(document.Name));
    }

    [Fact]
    public async Task AnUnreadableLedgerIsNeverWrittenOver()
    {
        var rig = Arranged();
        rig.Ledger.Unreadable = true;

        var outcome = await rig.RunOne();

        var failed = Assert.IsType<DocumentReadBack.Failed>(outcome);
        Assert.Equal(ReadBackStage.Persist, failed.Stage);
        Assert.IsType<LedgerUnreadableException>(failed.Failure);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Equal(0, rig.Results.Saves);
        Assert.Empty(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task ALedgerWithEntriesItCannotReadIsNeverWrittenOver()
    {
        // An entry of a kind this version does not know is part of the only copy: saving the readable ones would drop it.
        var rig = Arranged();
        rig.Ledger.UnreadableEntries = 1;

        var outcome = await rig.RunOne();

        var failed = Assert.IsType<DocumentReadBack.Failed>(outcome);
        Assert.Equal(ReadBackStage.Persist, failed.Stage);
        Assert.IsType<LedgerUnreadableException>(failed.Failure);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Equal(0, rig.Results.Saves);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Saved);
    }

    [Fact]
    public async Task AFailedLedgerSaveKeepsThePage()
    {
        var rig = Arranged();
        rig.Ledger.SaveFails = new IOException("disk full");

        var failed = Assert.IsType<DocumentReadBack.Failed>(await rig.RunOne());

        Assert.Equal(ReadBackStage.Persist, failed.Stage);
        Assert.IsType<IOException>(failed.Failure);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Saved);
    }

    [Fact]
    public async Task ARetryNextMorningSettlesIt()
    {
        var rig = Arranged();
        rig.Tablet.DownloadFails = new HttpRequestException("down");
        Assert.IsType<DocumentReadBack.Failed>(await rig.RunOne());

        rig.Tablet.DownloadFails = null;
        var second = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(second);
        Assert.Equal(SessionOutcome.Done, rig.Ledger.Held.OutcomeOf(Person, Day));
        Assert.Single(rig.Tablet.Trashed);
        Assert.Single(rig.Ledger.Held.Records);
    }

    [Fact]
    public async Task ATrashThatFailedIsRetriedWithoutReadingAgain()
    {
        var rig = Arranged();
        rig.Tablet.TrashFails = new HttpRequestException("busy");
        Assert.Equal(ReadBackStage.Trash, Assert.IsType<DocumentReadBack.Failed>(await rig.RunOne()).Stage);
        Assert.Equal(1, rig.Vision.Reads);

        rig.Tablet.TrashFails = null;
        var second = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(second);
        Assert.Equal(1, rig.Vision.Reads);
        Assert.Equal(1, rig.Tablet.Downloads);
        Assert.Equal([$"id-{Name}"], rig.Tablet.Trashed);
        Assert.Equal([Name], rig.Expected.Retired);
    }

    [Fact]
    public async Task ASavedPageStillHasItsQuestionsAskedBeforeItGoes()
    {
        var rig = Arranged(reply: document => WithDoubt(document, 2, "done", 0.5, marked: true));
        rig.Tablet.TrashFails = new HttpRequestException("busy");
        await rig.RunOne();
        rig.Notices.Board.Resolve(Assert.Single(rig.Notices.Board.Waiting()).CauseKey);
        rig.Tablet.TrashFails = null;
        rig.Calls.Calls.Clear();

        await rig.RunOne();

        Assert.Single(rig.Notices.Board.Waiting());
        InOrder(rig.Calls, "Board", "Trash");
    }

    [Fact]
    public async Task APageWhoseSavedResultHasGoneIsReadAgain()
    {
        var rig = Arranged();
        rig.Tablet.TrashFails = new HttpRequestException("busy");
        await rig.RunOne();
        rig.Tablet.TrashFails = null;
        rig.Results.Forget(Name);

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(2, rig.Vision.Reads);
    }

    // ---- absence, age, the digest ----

    [Fact]
    public async Task AnAbsentPageIsNotASkip()
    {
        var document = Workout();
        var rig = new Rig(true, document);
        rig.Tablet.Forget(document);
        rig.Vision.Reply = read => Read(read, PenStrokes.None);

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.NotOnTablet>(outcome);
        Assert.Equal(0, rig.Vision.Reads);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Empty(rig.Expected.Retired);
    }

    [Fact]
    public async Task ADocumentIsLetGoAfterAWeekAndLeftOnTheTablet()
    {
        var rig = Arranged();
        rig.Now = new DateTimeOffset(2026, 11, 11, 7, 15, 0, TimeSpan.FromHours(-5));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.GaveUp>(outcome);
        Assert.Equal([Name], rig.Expected.Retired);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Equal(0, rig.Tablet.Downloads);
    }

    [Fact]
    public async Task ADocumentIsStillTriedOnTheLastDay()
    {
        var rig = Arranged();
        rig.Now = new DateTimeOffset(2026, 11, 10, 7, 15, 0, TimeSpan.FromHours(-5));

        Assert.IsType<DocumentReadBack.Settled>(await rig.RunOne());
    }

    [Fact]
    public async Task DigestGoesAfterThreeDays()
    {
        var rig = new Rig(false, Digest());
        var name = Digest().Name;

        rig.Now = Morning.AddDays(1);
        var early = await rig.RunOne();
        Assert.IsType<DocumentReadBack.DigestKept>(early);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Retired);

        rig.Now = new DateTimeOffset(2026, 11, 6, 7, 15, 0, TimeSpan.FromHours(-5));
        var due = await rig.RunOne();

        Assert.IsType<DocumentReadBack.DigestTrashed>(due);
        Assert.Equal([$"id-{name}"], rig.Tablet.Trashed);
        Assert.Equal([name], rig.Expected.Retired);
    }

    [Fact]
    public async Task TheDaysAreCountedOnTheOwnersClockNotUtc()
    {
        // 10 pm on 5 November in the owner's zone is already 6 November in UTC. The digest of the 3rd is two days old to them.
        var rig = new Rig(false, Digest());
        rig.Now = new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.FromHours(-5));

        Assert.IsType<DocumentReadBack.DigestKept>(await rig.RunOne());
        Assert.Empty(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task ADigestIsFoundWithoutBeingDownloadedOrRead()
    {
        var rig = new Rig(false, Digest());
        rig.Now = new DateTimeOffset(2026, 11, 6, 7, 15, 0, TimeSpan.FromHours(-5));

        await rig.RunOne();

        Assert.Equal(0, rig.Tablet.Downloads);
        Assert.Contains("Locate", rig.Calls.Calls);
        Assert.Equal(0, rig.Vision.Reads);
        Assert.Equal(0, rig.Ledger.Saves);
    }

    [Fact]
    public async Task ADigestAlreadyGoneFromTheTabletIsLetGo()
    {
        var digest = Digest();
        var rig = new Rig(false, digest);
        rig.Tablet.Forget(digest);
        rig.Now = new DateTimeOffset(2026, 11, 6, 7, 15, 0, TimeSpan.FromHours(-5));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.NotOnTablet>(outcome);
        Assert.Equal([digest.Name], rig.Expected.Retired);
    }

    [Fact]
    public async Task AFailedDigestTrashIsTriedAgainTomorrow()
    {
        var rig = new Rig(false, Digest());
        rig.Now = new DateTimeOffset(2026, 11, 6, 7, 15, 0, TimeSpan.FromHours(-5));
        rig.Tablet.TrashFails = new HttpRequestException("busy");

        var failed = Assert.IsType<DocumentReadBack.Failed>(await rig.RunOne());
        Assert.Equal(ReadBackStage.Trash, failed.Stage);
        Assert.Empty(rig.Expected.Retired);

        rig.Tablet.TrashFails = null;
        Assert.IsType<DocumentReadBack.DigestTrashed>(await rig.RunOne());
    }

    // ---- meals ----

    private static readonly DateOnly MealDay = new(2026, 3, 14);

    [Fact]
    public async Task AMealsDocumentIsSavedAndNeverTouchesTheLedger()
    {
        var meals = Meals(MealDay);
        var rig = new Rig(true, meals) { Now = new DateTimeOffset(2026, 3, 14, 19, 0, 0, TimeSpan.FromHours(-4)) };
        rig.Vision.Reply = read => Read(read, PenStrokes.None);

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Contains(rig.Results.Load(meals.Name)!.All, resolution => resolution is InkResolution.ApprovedAtDeadline);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Equal(0, rig.Calls.Calls.Count(call => call == "LoadLedger"));
        Assert.Single(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task ADinnerBeforeItsDeadlineKeepsThePage()
    {
        var meals = Meals(MealDay);
        var rig = new Rig(true, meals) { Now = new DateTimeOffset(2026, 3, 14, 8, 0, 0, TimeSpan.FromHours(-4)) };
        rig.Vision.Reply = read => Read(read, PenStrokes.None);

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Unfinished>(outcome);
        Assert.Equal(0, rig.Results.Saves);
        Assert.Empty(rig.Tablet.Trashed);
    }

    // ---- the run as a whole ----

    [Fact]
    public async Task OneBadDocumentDoesNotStopTheNext()
    {
        var older = Workout(Day.AddDays(-1));
        var newer = Workout(Day);
        var rig = new Rig(false, newer, older);
        rig.Vision.Reply = read => Read(read, PenStrokes.Seen, (2, "done"));
        rig.Tablet.DownloadFailsFor = title => title == older.Name ? new HttpRequestException("down") : null;

        var report = await rig.Service.Run();

        Assert.IsType<DocumentReadBack.Failed>(report.Documents[0]);
        Assert.IsType<DocumentReadBack.Settled>(report.Documents[1]);
        Assert.Equal([newer.Name], rig.Expected.Retired);
        Assert.Equal(DiagnosticsOutcome.Partial, report.Outcome);
    }

    [Fact]
    public async Task DocumentsAreTakenOldestFirst()
    {
        var older = Workout(Day.AddDays(-2));
        var newer = Workout(Day);
        var rig = new Rig(true, newer, older);
        rig.Vision.Reply = read => Read(read, PenStrokes.Seen, (2, "done"));

        var report = await rig.Service.Run();

        Assert.Equal([older.Name, newer.Name], rig.Tablet.Downloaded);
        Assert.Equal([older.Name, newer.Name], report.Documents.Select(document => document.Name));
    }

    [Fact]
    public async Task ANothingToDoMorningSucceeds()
    {
        var rig = new Rig(false);

        var report = await rig.Service.Run();

        Assert.Empty(report.Documents);
        Assert.Equal(DiagnosticsOutcome.Succeeded, report.Outcome);
        Assert.Empty(rig.Log.Phrases);
    }

    [Fact]
    public async Task ACancelledRunStopsInsteadOfBecomingAFailure()
    {
        using var cancel = new CancellationTokenSource();
        var rig = Arranged();
        rig.Tablet.DownloadFailsFor = _ =>
        {
            cancel.Cancel();
            return new OperationCanceledException(cancel.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.Run(cancel.Token));

        Assert.Empty(rig.Expected.Retired);
        Assert.Empty(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task ATimedOutCallIsAFailureNotACancellation()
    {
        var rig = Arranged();
        rig.Tablet.DownloadFails = new TaskCanceledException("late", new TimeoutException());

        var failed = Assert.IsType<DocumentReadBack.Failed>(await rig.RunOne());

        Assert.Equal(ReadBackStage.Download, failed.Stage);
    }

    [Fact]
    public async Task AnUnauthorisedTabletIsReportedAsRefused()
    {
        var rig = Arranged();
        rig.Tablet.DownloadFails = new HttpRequestException("no", null, HttpStatusCode.Unauthorized);

        var report = await rig.Service.Run();

        Assert.Equal(DiagnosticsOutcome.Refused, report.Outcome);
    }

    // ---- the trail ----

    [Fact]
    public async Task EveryDocumentLeavesOneValueFreeLine()
    {
        var rig = Arranged(documents: [Workout(), Workout(Day.AddDays(-1))]);
        rig.Vision.Reply = read => WithDoubt(read, 2, "done", 0.5, marked: true);

        await rig.Service.Run();

        Assert.Equal(2, rig.Log.Phrases.Count);
        foreach (var phrase in rig.Log.Phrases)
        {
            var line = "[Nov 4, 7:15 am] INFO Body — " + phrase;
            Assert.Equal(line, TrailLines.Filter(line));
            Assert.DoesNotContain(Person, phrase, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Constitution", phrase, StringComparison.Ordinal);
            Assert.DoesNotContain("2026", phrase, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AFailureLineNamesTheStepAndOnlyTheExceptionType()
    {
        var rig = Arranged();
        rig.Vision.Fails = new HttpRequestException("secret detail from the service");

        await rig.RunOne();

        Assert.Equal(["ink read back failed at read: HttpRequestException, document kept"], rig.Log.Phrases);
    }

    private static DateTimeOffset Now(Rig rig) => rig.Now;
}
