using Constitution.Domain.Calendar;
using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Placement;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Documents;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The weekly workout run (F-2.2, F-2.3, F-5): reads the calendars, plans the week, adapts it, writes its events to the
/// calendar, draws each person's page for the day, puts it on the tablet and tells the read-back what to look for. Every
/// stage is a pure function or a seam that already existed; this only joins them in order and decides, at each seam, what
/// a failure means.
///
/// <para><b>The order.</b> Calendars, then busy time on the trip-aware calendars (<see cref="TripAwareCalendars"/>), then the
/// phased week (<see cref="PhasedWeek"/>: the assessment week for anyone who needs it, the planner for the rest), then
/// <see cref="RecoverySwap.Apply"/>, <see cref="SorenessPlan.Apply"/>, <see cref="ConditionsAdapter"/> (told who is away),
/// <see cref="TripAdapter"/> and <see cref="ProgressionPlan.ApplyCaps"/>, then <see cref="PlacementRunner"/>, then the pages.
/// Nothing is written until the week has been planned, and nothing is sent to the tablet until its event is on the calendar.</para>
///
/// <para><b>The week is planned as of its first instant on every run, and adapted as of now.</b> Planning from the week's start
/// gives the same plan on every run that sees the same calendars (the seam drops the app's own events, so they never change
/// it), and the adapters, which are the only things that should differ from run to run, take the real clock and leave a session
/// that has begun alone. Sessions of days already gone stay in the plan; the writer refuses them (<see cref="PastWriteGuard"/>),
/// so a run that comes late writes nothing into the past.</para>
///
/// <para><b>A session keeps its key however the week is re-planned</b> (B2). The week is re-made on every run, so a key that
/// meant "the nth session of its kind" changes the moment the owner's new Monday meeting takes the first one out of the plan:
/// every event after it is then held under the key of the day before. Keys are therefore taken from the record of issued numbers
/// (<see cref="KeyAssignments"/>, <see cref="WorkoutWeekSeams.KeyAssignments"/>): a session is named by its kind and its weekday,
/// keeps the number it was first given, and a new session takes a number above every one ever issued. Where the record cannot
/// vouch for a key the run does not guess: that person's sessions of that kind are left undecided
/// (<see cref="UnplacedReason.KeysUncertain"/>), nothing is written, read as deleted or paged for them, and the trail counts them.
/// A record that cannot be read stops placement as the record of written events does. The week must begin on a Monday.</para>
///
/// <para><b>The owner's moves are part of the base plan.</b> The Workouts tab moves a session by editing its event, and the plan
/// is re-derived here on every run, so the run is handed what the owner did (<see cref="WorkoutWeekRequest.PlanOverrides"/>) and
/// applies it right after the week is planned, before anything is adapted, placed or drawn. A moved session is planned where the
/// owner put it, under the key the tab gave its event, and its page follows it; a swap is read whole, each key before either
/// has moved. A skipped session stays in the plan (the keys after it must not move) but is never written, and its page is taken
/// off if one was sent. With none, the run is exactly what it was.</para>
///
/// <para><b>What goes to the calendar is the week as planned, not as adapted</b> (<see cref="WorkoutWeekRequest.PlaceAdaptedWeek"/>
/// switches it). The writer can create and never move or remove, and a key names a kind and a count, so a weather swap,
/// a recovery swap or a soreness rest applied before placement would, on any run after the first, write a second event
/// beside the first or renumber the keys. The calendar holds the household's reserved time; the paper, the Workouts tab and
/// the report say what is done in it.</para>
///
/// <para><b>Paper follows the calendar, and only the owner's word moves it.</b> A session gets a page only if its event is on
/// the calendar (found there, at whatever time the owner has it, or just written). One the owner deleted gets none, and a
/// page already sent for it is taken off if its slot has not begun. A session whose event cannot be known (the calendar could
/// not be looked into, the write failed, it could not be placed) gets no new page and loses none: not knowing is not a
/// delete, and a calendar that is unreachable must not empty the tablet.</para>
///
/// <para><b>A page the owner has begun is never replaced or taken off</b> (S3). The read-back reads a blank page as a skip, so before
/// a page is trashed or set down over, it is looked at (<see cref="IPageInkProbe"/>): ink on it, or no way to say, leaves it exactly where
/// it is (<see cref="DocumentVerdict.Kept"/>, and the trail says which). And a first send, which has no record of what is on the tablet,
/// looks the title up first rather than uploading a second document under it (<see cref="WorkoutDocumentDelivery"/>).</para>
///
/// <para><b>C-2.3.6.</b> A page already on the tablet is replaced only before its slot (<see cref="RedeliveryRules"/>), under
/// the same title, and what it says is judged by a fingerprint of the page without the voiced line, so a line that arrives
/// later is never a reason to send again. A session called off before its slot has its page taken off, since a blank page
/// left behind would be read the next morning as a skip the owner never made.</para>
///
/// <para><b>Idempotent.</b> A run on a week that has not changed finds every event held and every page unchanged, and writes,
/// sends, trashes and records nothing. A failure at a stage leaves only what was safely done: events written stay recorded
/// (placement saves in a <c>finally</c>), a page is recorded as delivered, and as waiting for its ink, only after the tablet
/// took it, and a refused page is simply not recorded, so the next run sends it.</para>
///
/// <para>Failures the network can name (a refused token, a timeout) come back in the report. A page that cannot be drawn is
/// reported for that page and does not stop the next. Anything else is a bug and propagates with its stack.</para>
/// </summary>
public sealed class WorkoutWeekRun(WorkoutWeekSeams seams)
{
    private readonly WorkoutWeekSeams seams = seams ?? throw new ArgumentNullException(nameof(seams));

    public async Task<WorkoutWeekOutcome> RunAsync(WorkoutWeekRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.DeliverAheadDays);
        if (request.WeekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw new ArgumentException(
                $"A workout week begins on a Monday, and {request.WeekStart:yyyy-MM-dd} is a {request.WeekStart.DayOfWeek}; a session's key names its weekday within the week.",
                nameof(request));
        }

        var rules = request.Rules;
        var adults = rules.Adults.Select(adult => adult.Name).ToList();
        if (adults.Count == 0)
        {
            return Finish(new WorkoutWeekOutcome.NotPlanned(NotPlannedBecause.NoOneToPlan, [], "The rules name no adult to plan for."));
        }

        var deliverFor = request.DeliverFor.Select(name => rules.FindAdult(name)?.Name
            ?? throw new ArgumentException($"'{name}' is not one of the household's adults, so no page can be delivered for them.", nameof(request)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var learning = seams.Learning.Load();
        if (learning.UnreadableBecause is { } unreadable)
        {
            return Finish(new WorkoutWeekOutcome.NotPlanned(NotPlannedBecause.RecordsUnreadable, [], unreadable));
        }

        var zone = rules.Zone;
        var first = request.WeekStart;
        var last = first.AddDays(6);
        var snapshot = await seams.Calendars.ReadAsync(ZoneClock.StartOfDay(first, zone), ZoneClock.StartOfDay(first.AddDays(7), zone), cancellation);

        var travel = await TravelPlan.ResolveAsync(TripDetector.Detect(rules, snapshot, first, last), seams.HotelGyms, cancellation);
        var aware = new TripAwareCalendars(snapshot, rules);
        var busy = adults.Select(name => BusyTime.For(name, first, last, rules, aware)).ToList();
        if (!busy.Any(item => item.IsComplete))
        {
            var causes = busy.SelectMany(item => item.Unreadable).Distinct().ToList();
            return Finish(new WorkoutWeekOutcome.NotPlanned(NotPlannedBecause.CalendarsUnreadable, causes, "No adult's calendars could be read in full, so nobody's free time is known."));
        }

        var planRequest = new WorkoutPlanRequest(
            first,
            rules,
            busy.Select(item => new PersonWeek(item) { Recovery = learning.Skips.RecoveryFor(item.Person, first) }).ToList(),
            request.Equipment,
            ZoneClock.StartOfDay(first, zone))
        {
            Settings = request.Settings,
        };
        var histories = adults.ToDictionary(name => name, learning.HistoryOf, StringComparer.OrdinalIgnoreCase);
        var phased = await PhasedWeek.PlanAsync(planRequest, histories, seams.Planner, cancellation);
        var numbers = seams.KeyAssignments.Load();
        phased = WithTheOwnersMoves(phased, request.PlanOverrides, numbers.IsReadable ? numbers.Assignments : null);
        RecordAssessmentWeeks(first, adults, histories, phased);

        var now = request.Now;
        var today = ZoneClock.DayOf(now, zone);
        var adaptRequest = planRequest with { Now = now };
        var reads = seams.Recovery.Read(adults, today);
        var recovered = RecoverySwap.Apply(phased.Plan, reads, today, now, request.Equipment);
        var sore = SorenessPlan.Apply(recovered.Plan, learning.Soreness, request.Equipment);
        var limits = request.Conditions ?? new ConditionsLimits { ShowerRoom = request.Placement.ShowerBuffer };
        var weather = await ConditionsAdapter.AdaptAsync(adaptRequest, sore.Plan, seams.Conditions, cancellation, limits, travel.IsAway);
        var travelled = TripAdapter.Apply(adaptRequest, weather.Plan, travel);
        var adapted = ProgressionPlan.ApplyCaps(travelled.Plan, learning.Progression);

        var placed = request.PlaceAdaptedWeek ? adapted : phased.Plan;
        var issued = numbers.IsReadable ? RememberSkips(placed, numbers.Assignments, request.PlanOverrides) : numbers.Assignments;
        var runner = new PlacementRunner(new PastWriteGuard(seams.CalendarWriter, () => now), seams.WrittenKeys, seams.KeyAssignments);
        var placement = await runner.RunAsync(
            new PlacementRequest(first, busy, request.HouseholdEvents, placed) { Options = request.Placement },
            cancellation);

        var context = new DayContext(request, learning, phased, sore.Adjustments, reads, adapted, new PlacementIndex(placed, issued), placement, zone);
        var documents = new List<DocumentOutcome>();
        var days = Enumerable.Range(0, request.DeliverAheadDays + 1).Select(today.AddDays).Where(day => day >= first && day <= last).ToList();
        foreach (var person in deliverFor)
        {
            foreach (var day in days)
            {
                cancellation.ThrowIfCancellationRequested();
                if (await DocumentFor(context, person, day, cancellation) is { } outcome)
                {
                    seams.Log.Outcome(WorkoutWeekTrail.LogPhrase(outcome));
                    documents.Add(outcome);
                }
            }
        }

        return Finish(new WorkoutWeekOutcome.Planned(new WorkoutWeekReport(
            adaptRequest,
            adapted,
            placed,
            phased.Phases,
            phased.Moderate?.Origin,
            recovered.Swaps,
            sore.Adjustments,
            weather.Changes,
            weather.ConditionsWereRead,
            travelled.Changes,
            travelled.Notes,
            busy.Where(item => !item.IsComplete).Select(item => item.Person).ToList(),
            placement,
            documents)));
    }

    // The base plan is re-derived on every run, so what the owner did to it on the Workouts tab is applied to it here, before
    // anything is adapted, placed or drawn: the moved session is planned where the owner put it, under the key the tab gave its
    // event, and the page follows it. An assessment session that moved is the same session on its new day.
    private PhasedWeekPlan WithTheOwnersMoves(PhasedWeekPlan phased, PlanOverrides overrides, KeyAssignments? issued)
    {
        if (overrides.IsEmpty)
        {
            return phased;
        }

        var applied = overrides.ApplyTo(phased.Plan, issued);
        if (applied.Unresolved.Count > 0)
        {
            seams.Log.Outcome(WorkoutWeekTrail.MoveNotFoundPhrase);
        }

        var assessments = phased.Assessments
            .Select(item => applied.Relocated.FirstOrDefault(move => ReferenceEquals(move.Old, item.Placed)) is { } moved ? item with { Placed = moved.New } : item)
            .ToList();
        return phased with { Plan = applied.Plan, Assessments = assessments };
    }

    // A session the owner skipped stays in the plan (so the keys after it do not move) but must never be written. Placement
    // already reads "written and not on the calendar" as the owner's delete, so the skipped session's key is put in the record
    // of written ones. A record that cannot be read is left alone: treating it as empty is how every deleted event comes back.
    // The skipped session's number is kept first: a key recorded as written that no record issued is exactly what placement
    // refuses to read as a delete. Returns the record of issued numbers as it now stands.
    private KeyAssignments RememberSkips(HouseholdWorkoutPlan placed, KeyAssignments issued, PlanOverrides overrides)
    {
        var skipped = new List<KeyIssue>();
        var numbered = SessionKeys.For(placed, issued);
        foreach (var skip in overrides.Skips)
        {
            if (numbered.IssueOn(skip.Person, skip.Day) is { } issue)
            {
                skipped.Add(issue);
            }
        }

        var record = seams.WrittenKeys.Load();
        if (skipped.Count == 0 || !record.IsReadable)
        {
            return issued;
        }

        var kept = issued.With(skipped);
        if (!ReferenceEquals(kept, issued))
        {
            seams.KeyAssignments.Save(kept);
        }

        var keys = record.Keys.With(skipped.Select(issue => issue.Key));
        if (!ReferenceEquals(keys, record.Keys))
        {
            seams.WrittenKeys.Save(keys);
        }

        return kept;
    }

    private WorkoutWeekOutcome Finish(WorkoutWeekOutcome outcome)
    {
        seams.Log.Outcome(WorkoutWeekTrail.LogPhrase(outcome));
        return outcome;
    }

    // The caller's job when a week is planned as someone's assessment: write it down, so the next run (the same week) reads
    // "underway" and the next week reads "established". Only when they got a session: a week that held none must not use up
    // the first-install assessment.
    private void RecordAssessmentWeeks(DateOnly weekStart, IReadOnlyList<string> adults, Dictionary<string, TrainingHistory> histories, PhasedWeekPlan phased)
    {
        foreach (var person in adults)
        {
            var assessed = phased.Phases[person].Phase == WorkoutPhase.Assessment;
            var hasSessions = phased.Assessments.Any(item => item.Placed.Participants.Any(name => Same(name, person)));
            if (assessed && hasSessions && histories[person].AssessmentWeekStart != weekStart)
            {
                seams.Learning.RecordAssessmentWeek(person, weekStart);
            }
        }
    }

    // ---- one person's page for one day ----

    private sealed record DayContext(
        WorkoutWeekRequest Request,
        WorkoutLearning Learning,
        PhasedWeekPlan Phased,
        IReadOnlyList<SessionAdjustment> Adjustments,
        IReadOnlyDictionary<string, RecoveryRead> Reads,
        HouseholdWorkoutPlan Adapted,
        PlacementIndex Index,
        PlacementRun Placement,
        TimeZoneInfo Zone);

    private sealed record Built(byte[] Pdf, string Fingerprint, ExpectedDocument Expected, bool CarriesDoctorNote);

    private async Task<DocumentOutcome?> DocumentFor(DayContext context, string person, DateOnly day, CancellationToken cancellation)
    {
        var now = context.Request.Now;
        var name = DeliveredName.For(DeliveryKind.Workout, day, person);
        var previous = seams.Delivered.Find(name);
        var session = context.Adapted.PlanFor(person)?.SessionsOn(day).FirstOrDefault();
        var skipped = context.Request.PlanOverrides.IsSkipped(person, day);
        var (standing, slot) = session is null || skipped ? (CalendarStanding.Gone, null) : StandingOnTheCalendar(context, person, day);
        if (standing == CalendarStanding.Unknown)
        {
            // Not knowing is not a delete: a page already on the tablet is left exactly where it is, and none is made.
            return new DocumentOutcome(name, person, day, DocumentVerdict.NotOnCalendar);
        }

        Built? built = null;
        if (session is not null && slot is { } start)
        {
            var voice = await EncouragementFor(context, person, session, cancellation);
            try
            {
                built = Build(context, person, day, session, start, voice, previous);
            }
            catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
            {
                return new DocumentOutcome(name, person, day, DocumentVerdict.NotBuilt, failure);
            }
        }

        var current = built is null || session is null || slot is null ? null : new CurrentWorkout(built.Fingerprint, slot.Value, slot.Value + session.Session.Duration);
        switch (RedeliveryRules.Decide(previous, current, now))
        {
            case Redelivery.Nothing:
                return session is null ? null : new DocumentOutcome(name, person, day, DocumentVerdict.NotOnCalendar);
            case Redelivery.LeaveAlone:
                return new DocumentOutcome(name, person, day, DocumentVerdict.Unchanged);
            case Redelivery.TooLate:
                return new DocumentOutcome(name, person, day, DocumentVerdict.TooLate);
            case Redelivery.Withdraw:
                return await Withdraw(name, person, day, cancellation);
            case Redelivery.Send:
                return await Send(person, day, session!, slot!.Value, built!, previous, replace: false, now, cancellation);
            case Redelivery.Replace:
                return await Send(person, day, session!, slot!.Value, built!, previous, replace: true, now, cancellation);
            default:
                throw new InvalidOperationException("The redelivery rules gave an answer this run does not know.");
        }
    }

    private async Task<DocumentOutcome> Send(
        string person, DateOnly day, PlannedSession session, DateTimeOffset slot, Built built, DeliveredWorkout? previous, bool replace, DateTimeOffset now, CancellationToken cancellation)
    {
        var delivery = new WorkoutDocumentDelivery(seams.Delivery, seams.Tablet, seams.InkProbe);

        // A replacement is only ever asked for before the slot (RedeliveryRules). A first send has no record to say what is on the
        // tablet, so it looks the title up; a blank page it finds is replaced only while the slot has not begun.
        var sent = replace
            ? await delivery.ReplaceAsync(day, person, built.Pdf, cancellation)
            : await delivery.SendAsync(day, person, built.Pdf, mayReplaceBlank: now < slot, cancellation);
        if (sent.Kept is { } kept)
        {
            return new DocumentOutcome(sent.Name, person, day, DocumentVerdict.Kept, kept.Failure, kept.Ink);
        }

        var result = sent.Result;
        if (!result.Delivered)
        {
            return new DocumentOutcome(result.Name, person, day, DocumentVerdict.NotDelivered, result.Failure);
        }

        // The tablet has it. The read-back's record first (without it the page is never read), then ours.
        try
        {
            seams.Expected.Record(built.Expected);
            seams.Delivered.Record(new DeliveredWorkout(result.Name, person, day, session.Session.Kind, slot, built.Fingerprint, built.CarriesDoctorNote));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new DocumentOutcome(result.Name, person, day, DocumentVerdict.NotDelivered, failure);
        }

        if (built.CarriesDoctorNote && previous?.CarriesDoctorNote != true)
        {
            seams.Learning.RecordDoctorNotePrinted(person);
        }

        return new DocumentOutcome(result.Name, person, day, replace ? DocumentVerdict.Replaced : DocumentVerdict.Delivered);
    }

    private async Task<DocumentOutcome> Withdraw(string name, string person, DateOnly day, CancellationToken cancellation)
    {
        var withdrawn = await new WorkoutDocumentDelivery(seams.Delivery, seams.Tablet, seams.InkProbe).WithdrawAsync(day, person, cancellation);
        if (withdrawn.Kept is { } kept)
        {
            return new DocumentOutcome(name, person, day, DocumentVerdict.Kept, kept.Failure, kept.Ink);
        }

        if (withdrawn.Failure is { } failure)
        {
            return new DocumentOutcome(name, person, day, DocumentVerdict.NotDelivered, failure);
        }

        try
        {
            seams.Expected.Retire(name);
            seams.Delivered.Remove(name);
        }
        catch (Exception recordFailure) when (recordFailure is IOException or UnauthorizedAccessException)
        {
            return new DocumentOutcome(name, person, day, DocumentVerdict.NotDelivered, recordFailure);
        }

        return new DocumentOutcome(name, person, day, DocumentVerdict.Withdrawn);
    }

    private enum CalendarStanding
    {
        /// <summary>The event is on the calendar (found there at the owner's time, or just written).</summary>
        OnCalendar,

        /// <summary>There is no session, or the owner deleted its event: the day has no workout.</summary>
        Gone,

        /// <summary>Nobody can say: the calendar could not be looked into or written, the session could not be placed, or the record of written events could not be read.</summary>
        Unknown,
    }

    // Where the person's event for the day stands. A delete is the owner's word and takes the page away; a calendar nobody
    // could look into, or a write that failed, is not a word at all, and must not pull a page already on the tablet.
    private static (CalendarStanding Standing, DateTimeOffset? Slot) StandingOnTheCalendar(DayContext context, string person, DateOnly day)
    {
        if (context.Index.KeyFor(person, day) is not { } key || context.Placement.Plan is not { } plan)
        {
            return (CalendarStanding.Unknown, null);
        }

        var outcome = plan.Outcomes.FirstOrDefault(item => item.Key == key && Same(item.Person, person));
        return outcome switch
        {
            EventHeld held => (CalendarStanding.OnCalendar, held.Start),
            EventToCreate make when context.Placement.Created.Any(item => item.Key == make.Key && Same(item.Person, make.Person)) => (CalendarStanding.OnCalendar, make.Start),
            EventSkipped => (CalendarStanding.Gone, null),
            _ => (CalendarStanding.Unknown, null),
        };
    }

    private async Task<IReadOnlyList<EncouragementLine?>?> EncouragementFor(DayContext context, string person, PlannedSession session, CancellationToken cancellation)
    {
        if (session.Session.Kind != SessionKind.Strength || IsAssessment(context, person, session))
        {
            return null;
        }

        var lines = new List<EncouragementLine?>();
        foreach (var prescription in ProgressionPlan.Prescribe(context.Learning.Progression, person, session.Session, context.Learning.BaselinesOf(person)))
        {
            lines.Add(prescription.News is { } news ? await seams.Encouragement.LineAsync(news, context.Request.Now, cancellation) : null);
        }

        return lines;
    }

    private static AssessmentSession? AssessmentOf(DayContext context, string person, PlannedSession session) =>
        context.Phased.Assessments.FirstOrDefault(item =>
            item.Placed.Day == session.Day
            && item.Placed.Participants.Any(name => Same(name, person))
            && item.Placed.Session.Kind == session.Session.Kind);

    private static bool IsAssessment(DayContext context, string person, PlannedSession session) => AssessmentOf(context, person, session) is not null;

    private static Built Build(
        DayContext context, string person, DateOnly day, PlannedSession session, DateTimeOffset start, IReadOnlyList<EncouragementLine?>? voice, DeliveredWorkout? previous)
    {
        if (AssessmentOf(context, person, session) is { } assessment)
        {
            var mine = context.Phased.Assessments
                .Where(item => item.Placed.Participants.Any(name => Same(name, person)))
                .OrderBy(item => item.Placed.Day)
                .ToList();
            var others = mine.Where(item => item != assessment).Select(item => item.Placed.Day).ToList();
            var model = AssessmentDocumentBuilder.Build(assessment, start, context.Zone, mine.IndexOf(assessment) + 1, mine.Count, others);
            var pdf = ConstitutionDocumentRenderer.Render(AssessmentDocument.Build(model));
            return new Built(pdf, DocumentFingerprint.Of(pdf), AssessmentDocumentBuilder.Expected(model, assessment, person, day, person), false);
        }

        var doses = session.Session.Kind == SessionKind.Strength
            ? ProgressionPlan.Prescribe(context.Learning.Progression, person, session.Session, context.Learning.BaselinesOf(person))
            : null;
        var soreness = context.Learning.Soreness.PageModel(person, day, context.Reads.FirstOrDefault(pair => Same(pair.Key, person)).Value);

        // "Once" means once per soreness, not once per copy of the page: a replacement keeps the note the first copy carried.
        if (soreness is not null && previous?.CarriesDoctorNote == true)
        {
            soreness = soreness with { DoctorNoteDue = true };
        }

        var substitutions = context.Adjustments
            .Where(item => Same(item.Person, person) && item.Day == day)
            .SelectMany(item => item.Substitutions)
            .ToList();
        var workout = WorkoutDocumentBuilder.Build(session, start, context.Zone, doses, voice, soreness, substitutions);
        var bytes = ConstitutionDocumentRenderer.Render(WorkoutDocument.Build(workout));
        var saysTheSame = voice is null || voice.All(line => line is null)
            ? bytes
            : ConstitutionDocumentRenderer.Render(WorkoutDocument.Build(WorkoutDocumentBuilder.WithoutVoice(workout)));
        return new Built(
            bytes,
            DocumentFingerprint.Of(saysTheSame),
            ExpectedDocument.ForWorkout(workout, person, session.Session.Kind, day, person),
            workout.Soreness?.DoctorNoteDue == true);
    }

    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
