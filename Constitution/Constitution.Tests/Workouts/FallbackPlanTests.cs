using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// C-5.1.4 · A rules-only plan when the assistant is away (R10). Unavailable at planning time means the same rules
/// give the week, and nothing on the page or phone says so: the only record is the origin the caller keeps for
/// Diagnostics. An authored plan is held to the same rules before it is used, so the seam cannot loosen them.
/// </summary>
public class FallbackPlanTests
{
    private static string[] Shape(IEnumerable<PlannedSession> sessions) =>
        sessions.Select(item => $"{item.Person()} {item.Day:yyyy-MM-dd} {item.Start:HH:mm} {item.Session.Kind} {item.Session.Duration.TotalMinutes}").ToArray();

    private static PlanWithFallback Offline(RecordingLog log, AuthorUnavailable reason = AuthorUnavailable.Offline) =>
        new(new ScriptedAuthor(new PlanAuthorResult.Unavailable(reason)), log);

    // ---- TemplateWeekObeysTheSameRules ----

    [Fact]
    public async Task TemplateWeekObeysTheSameRules()
    {
        var planned = 0;
        for (var seed = 1; seed <= 40; seed++)
        {
            var random = new Random(seed);
            var shift = random.Next(2) * 7;
            var calendars = Calendars(Entries(random, shift), Entries(random, shift));
            var equipment = EquipmentSet.Of(EquipmentCatalogue.All.Where(item => random.Next(2) == 0).Select(item => item.Id));
            var request = Request(calendars, equipment, weekShift: shift);

            var outcome = await Offline(new RecordingLog()).PlanAsync(request, CancellationToken.None);

            var where = $"seed {seed}";
            planned += outcome.Plan.Plans.Sum(item => item.Sessions.Count);
            Assert.Equal(PlanOrigin.RulesOnly, outcome.Origin);
            Assert.Empty(PlanRules.Violations(request, outcome.Plan));
            Assert.True(
                Shape(WorkoutPlanner.Plan(request).Plans.SelectMany(item => item.Sessions)).SequenceEqual(Shape(outcome.Plan.Plans.SelectMany(item => item.Sessions))),
                $"{where}: not the rules planner's week");
        }

        Assert.True(planned > 100, $"only {planned} sessions across the generated weeks");
    }

    [Fact]
    public async Task TheTemplateWeekIsPlacedNotEmpty()
    {
        var outcome = await Offline(new RecordingLog()).PlanAsync(Request(Calendars()), CancellationToken.None);

        Assert.NotEmpty(SamOf(outcome.Plan).Sessions);
        Assert.NotEmpty(LeeOf(outcome.Plan).Sessions);
        Assert.Equal(Day(0), outcome.Plan.WeekStart);
    }

    // ---- NothingOnThePageSaysSo ----

    [Fact]
    public async Task NothingOnThePageSaysSo()
    {
        var calendars = Calendars(sam: BlockDays(0, 1, 2, 3, 4, 5, 6));
        var request = Request(calendars);
        var fallback = await Offline(new RecordingLog()).PlanAsync(request, CancellationToken.None);
        var authored = await new PlanWithFallback(
            new ScriptedAuthor(new PlanAuthorResult.Authored(WorkoutPlanner.Plan(request))),
            new RecordingLog()).PlanAsync(request, CancellationToken.None);

        // The plan the page is made from is the same value either way; origin lives only on the outcome.
        Assert.Equal(PlanOrigin.RulesOnly, fallback.Origin);
        Assert.Equal(PlanOrigin.Authored, authored.Origin);
        Assert.Equal(Shape(authored.Plan.Plans.SelectMany(item => item.Sessions)), Shape(fallback.Plan.Plans.SelectMany(item => item.Sessions)));
        Assert.Equal(SamOf(authored.Plan).NothingBecause, SamOf(fallback.Plan).NothingBecause);
        Assert.NotNull(SamOf(fallback.Plan).NothingBecause);
        Assert.Equal(authored.Plan.Plans.Select(item => item.Person).ToArray(), fallback.Plan.Plans.Select(item => item.Person).ToArray());

        var words = new[] { "template", "fallback", "unavailable", "offline", "rules", "author", "assistant", "claude", "origin" };
        foreach (var text in PageText(fallback.Plan))
        {
            foreach (var word in words)
            {
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void NoTypeThePageIsMadeFromCarriesAnOrigin()
    {
        Type[] pageTypes = [typeof(HouseholdWorkoutPlan), typeof(WeeklyPlan), typeof(PlannedSession), typeof(Session), typeof(Exercise)];
        string[] words = ["Origin", "Fallback", "Template", "Unavailable", "RulesOnly", "Author", "Source"];

        foreach (var type in pageTypes)
        {
            foreach (var member in type.GetMembers().Select(item => item.Name))
            {
                foreach (var word in words)
                {
                    Assert.DoesNotContain(word, member, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void TheFallbackReachesNothingButTheLog()
    {
        var parameters = typeof(PlanWithFallback).GetConstructors().Single().GetParameters().Select(item => item.ParameterType).ToArray();

        Assert.Equal([typeof(IPlanAuthor), typeof(IPlanLog)], parameters);
        Assert.Equal(typeof(void), typeof(IPlanLog).GetMethod(nameof(IPlanLog.Record))!.ReturnType);
    }

    // ---- Diagnostics records it ----

    [Fact]
    public async Task DiagnosticsRecordsTheFallbackOnce()
    {
        var log = new RecordingLog();

        var outcome = await Offline(log, AuthorUnavailable.TimedOut).PlanAsync(Request(Calendars()), CancellationToken.None);

        Assert.Equal(AuthorUnavailable.TimedOut, outcome.Reason);
        Assert.Equal([(PlanOrigin.RulesOnly, (AuthorUnavailable?)AuthorUnavailable.TimedOut)], log.Entries);
    }

    [Theory]
    [InlineData(AuthorUnavailable.NotConfigured)]
    [InlineData(AuthorUnavailable.Offline)]
    [InlineData(AuthorUnavailable.TimedOut)]
    [InlineData(AuthorUnavailable.KeyRejected)]
    [InlineData(AuthorUnavailable.ReplyUnusable)]
    [InlineData(AuthorUnavailable.BrokeTheRules)]
    public void TheDiagnosticsLineNamesTheReasonAndNothingElse(AuthorUnavailable reason)
    {
        var line = PlanDiagnostics.Line(PlanOrigin.RulesOnly, reason);

        Assert.StartsWith("Workout plan: rules only", line);
        Assert.Contains(reason.ToString().ToLowerInvariant(), line.Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant());
        Assert.DoesNotContain(Sam, line);
        Assert.DoesNotContain(Lee, line);
        Assert.Matches("^[A-Za-z :()-]+$", line);
    }

    [Fact]
    public void AnAuthoredWeekHasItsOwnLine()
    {
        Assert.Equal("Workout plan: authored", PlanDiagnostics.Line(PlanOrigin.Authored, null));
    }

    [Fact]
    public async Task AnAuthoredWeekIsRecordedToo()
    {
        var log = new RecordingLog();
        var request = Request(Calendars());

        var outcome = await new PlanWithFallback(new ScriptedAuthor(new PlanAuthorResult.Authored(WorkoutPlanner.Plan(request))), log)
            .PlanAsync(request, CancellationToken.None);

        Assert.Equal(PlanOrigin.Authored, outcome.Origin);
        Assert.Null(outcome.Reason);
        Assert.Equal([(PlanOrigin.Authored, (AuthorUnavailable?)null)], log.Entries);
    }

    // ---- the seam ----

    [Fact]
    public async Task AnAuthoredWeekThatObeysIsUsedAsItCame()
    {
        var request = Request(Calendars());
        var ordinary = WorkoutPlanner.Plan(request);
        // An author that drops Lee's sessions entirely is still within the rules (fewer is never a breach).
        var lighter = ordinary with { Plans = [SamOf(ordinary), LeeOf(ordinary) with { Sessions = [] }], JointSessions = [] };

        var outcome = await new PlanWithFallback(new ScriptedAuthor(new PlanAuthorResult.Authored(lighter)), new RecordingLog())
            .PlanAsync(request, CancellationToken.None);

        Assert.Equal(PlanOrigin.Authored, outcome.Origin);
        Assert.Same(lighter, outcome.Plan);
    }

    [Fact]
    public async Task AnAuthoredWeekThatBreaksTheRulesIsNotUsed()
    {
        var request = Request(Calendars());
        var ordinary = WorkoutPlanner.Plan(request);
        var sam = SamOf(ordinary);
        var broken = sam.Sessions[0] with { Start = sam.Sessions[0].Gap.Start.AddHours(-3) };
        var bad = ordinary with { Plans = [sam with { Sessions = [broken, .. sam.Sessions.Skip(1)] }, LeeOf(ordinary)] };
        var log = new RecordingLog();

        var outcome = await new PlanWithFallback(new ScriptedAuthor(new PlanAuthorResult.Authored(bad)), log).PlanAsync(request, CancellationToken.None);

        Assert.Equal(PlanOrigin.RulesOnly, outcome.Origin);
        Assert.Equal(AuthorUnavailable.BrokeTheRules, outcome.Reason);
        Assert.Equal(Shape(ordinary.Plans.SelectMany(item => item.Sessions)), Shape(outcome.Plan.Plans.SelectMany(item => item.Sessions)));
        Assert.Equal([(PlanOrigin.RulesOnly, (AuthorUnavailable?)AuthorUnavailable.BrokeTheRules)], log.Entries);
    }

    [Fact]
    public async Task TheNullAuthorMeansEveryWeekIsRulesOnly()
    {
        var log = new RecordingLog();

        var outcome = await new PlanWithFallback(new NoPlanAuthor(), log).PlanAsync(Request(Calendars()), CancellationToken.None);

        Assert.Equal(PlanOrigin.RulesOnly, outcome.Origin);
        Assert.Equal(AuthorUnavailable.NotConfigured, outcome.Reason);
    }

    [Fact]
    public async Task TheCancellationReachesTheAuthor()
    {
        var author = new ScriptedAuthor(new PlanAuthorResult.Unavailable(AuthorUnavailable.Offline));
        using var source = new CancellationTokenSource();

        await new PlanWithFallback(author, new RecordingLog()).PlanAsync(Request(Calendars()), source.Token);

        Assert.Equal(1, author.Calls);
        Assert.Equal(source.Token, author.LastToken);
    }

    [Fact]
    public async Task AnAuthorThatThrowsIsNotHidden()
    {
        var log = new RecordingLog();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlanWithFallback(new ThrowingAuthor(), log).PlanAsync(Request(Calendars()), CancellationToken.None));

        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task ARequestWithNoOneIsRefused()
    {
        var request = Request(Calendars()) with { People = [] };

        await Assert.ThrowsAsync<ArgumentException>(() => Offline(new RecordingLog()).PlanAsync(request, CancellationToken.None));
    }

    // ---- the rules, one at a time (so the check itself cannot go slack) ----

    private static (WorkoutPlanRequest Request, HouseholdWorkoutPlan Plan, PlannedSession Strength) SamsWeek(EquipmentSet? equipment = null)
    {
        var request = Request(Calendars(), equipment, bothAdults: false);
        var plan = WorkoutPlanner.Plan(request);
        return (request, plan, SamOf(plan).Sessions.First(item => item.Session.Kind == SessionKind.Strength));
    }

    private static HouseholdWorkoutPlan Swapped(HouseholdWorkoutPlan plan, PlannedSession old, params PlannedSession[] replacement)
    {
        var sam = SamOf(plan);
        var sessions = sam.Sessions.SelectMany(item => ReferenceEquals(item, old) ? replacement : [item]).OrderBy(item => item.Start).ToList();
        return plan with { Plans = [sam with { Sessions = sessions }] };
    }

    [Fact]
    public void AGoodWeekHasNoViolations()
    {
        var (request, plan, _) = SamsWeek();

        Assert.NotEmpty(SamOf(plan).Sessions);
        Assert.Empty(PlanRules.Violations(request, plan));
    }

    [Fact]
    public void ASessionOutsideItsGapIsCaught()
    {
        var (request, plan, strength) = SamsWeek();

        var found = PlanRules.Violations(request, Swapped(plan, strength, strength with { Start = strength.Gap.End.AddMinutes(-5) }));

        Assert.Contains(new PlanViolation(Sam, strength.Day, PlanRule.SessionLeavesItsGap), found);
    }

    [Fact]
    public void AGapUnderThirtyMinutesIsCaught()
    {
        var (request, plan, strength) = SamsWeek();
        var tight = strength.Gap with { End = strength.Gap.Start.AddMinutes(29) };
        var shortened = strength with { Gap = tight, Session = new Session(SessionKind.Mobility, TimeSpan.FromMinutes(20), false, strength.Session.Exercises) };

        var found = PlanRules.Violations(request, Swapped(plan, strength, shortened));

        Assert.Contains(new PlanViolation(Sam, strength.Day, PlanRule.GapTooShort), found);
    }

    [Fact]
    public void ASessionOverBusyTimeIsCaught()
    {
        var (request, plan, _) = SamsWeek();
        var onMonday = SamOf(plan).Sessions.First();
        // Sam works 8 to 4 on weekdays, so the middle of the working day is busy whatever gap is claimed.
        var wide = onMonday.Gap with { Start = At(DayIndex(onMonday), 9), End = At(DayIndex(onMonday), 11) };
        var overWork = onMonday with { Gap = wide, Start = At(DayIndex(onMonday), 9) };

        var found = PlanRules.Violations(request, Swapped(plan, onMonday, overWork));

        Assert.Contains(new PlanViolation(Sam, onMonday.Day, PlanRule.BusyTime), found);
    }

    [Fact]
    public void ASessionBeforeWakingIsCaught()
    {
        var (request, plan, _) = SamsWeek();
        var any = SamOf(plan).Sessions.First();
        var day = DayIndex(any);
        var early = any with { Gap = new FreeGap(At(day, 5), At(day, 6, 45)), Start = At(day, 5, 30) };

        var found = PlanRules.Violations(request, Swapped(plan, any, early));

        Assert.Contains(new PlanViolation(Sam, any.Day, PlanRule.OutsideWakingHours), found);
    }

    [Fact]
    public void ASessionInThePastIsCaught()
    {
        var (request, plan, _) = SamsWeek();
        var late = request with { Now = SamOf(plan).Sessions[0].Start.AddMinutes(1) };

        var found = PlanRules.Violations(late, plan);

        Assert.Contains(found, item => item.Rule == PlanRule.InThePast && item.Person == Sam);
    }

    [Fact]
    public void AnUntickedItemIsCaught()
    {
        var (request, plan, _) = SamsWeek(EquipmentSet.Everything());
        var bare = request with { Equipment = EquipmentSet.Of([]) };

        var found = PlanRules.Violations(bare, plan);

        Assert.Contains(found, item => item.Rule == PlanRule.UntickedEquipment);
    }

    [Fact]
    public void AHardSessionOnASundayIsCaught()
    {
        var (request, plan, strength) = SamsWeek();
        var sunday = strength with { Day = Day(6), Gap = new FreeGap(At(6, 9), At(6, 11)), Start = At(6, 9) };

        var found = PlanRules.Violations(request, Swapped(plan, strength, sunday));

        Assert.Contains(new PlanViolation(Sam, Day(6), PlanRule.SundayNotGentle), found);
    }

    [Fact]
    public void TwoSessionsOnOneDayAreCaught()
    {
        var (request, plan, strength) = SamsWeek();
        var twin = strength with { Start = strength.Start.AddMinutes(5) };

        var found = PlanRules.Violations(request, Swapped(plan, strength, strength, twin));

        Assert.Contains(found, item => item.Rule == PlanRule.TwoOnOneDay && item.Person == Sam);
    }

    [Fact]
    public void HardSessionsOnConsecutiveDaysAreCaught()
    {
        var (request, plan, strength) = SamsWeek();
        var next = strength with { Day = strength.Day.AddDays(1), Gap = strength.Gap with { Start = strength.Gap.Start.AddDays(1), End = strength.Gap.End.AddDays(1) }, Start = strength.Start.AddDays(1) };

        var found = PlanRules.Violations(request, Swapped(plan, strength, strength, next));

        Assert.Contains(found, item => item.Rule == PlanRule.HardOnConsecutiveDays && item.Person == Sam);
    }

    [Fact]
    public void AHardDayRightAfterTheLastHardOneIsCaught()
    {
        var (request, plan, strength) = SamsWeek();
        var recent = request with
        {
            People = [request.People[0] with { Recovery = new RecoveryInputs { LastHardSessionDay = strength.Day.AddDays(-1) } }],
        };

        var found = PlanRules.Violations(recent, plan);

        Assert.Contains(found, item => item.Rule == PlanRule.HardOnConsecutiveDays && item.Person == Sam);
    }

    [Fact]
    public void ASessionOutsideTheWeekIsCaught()
    {
        var (request, plan, strength) = SamsWeek();
        var after = strength with
        {
            Day = Day(7),
            Gap = strength.Gap with { Start = strength.Gap.Start.AddDays(7), End = strength.Gap.End.AddDays(7) },
            Start = strength.Start.AddDays(7),
        };

        var found = PlanRules.Violations(request, Swapped(plan, strength, after));

        Assert.Contains(new PlanViolation(Sam, Day(7), PlanRule.OutsideTheWeek), found);
    }

    [Fact]
    public void APersonNotInTheRequestIsCaught()
    {
        var (request, plan, _) = SamsWeek();
        var stranger = plan with { Plans = [.. plan.Plans, SamOf(plan) with { Person = "Zed" }] };

        var found = PlanRules.Violations(request, stranger);

        Assert.Contains(found, item => item.Rule == PlanRule.NotInTheRequest && item.Person == "Zed");
    }

    // ---- helpers ----

    private static IEnumerable<string> PageText(HouseholdWorkoutPlan plan) =>
        plan.Plans.SelectMany(week => new[] { week.NothingBecause ?? string.Empty }
            .Concat(week.Sessions.SelectMany(item => item.Session.Exercises.Select(exercise => exercise.Name))));

    private static IEnumerable<CalendarEntry> Entries(Random random, int shift)
    {
        var entries = new List<CalendarEntry>();
        for (var index = 0; index < random.Next(0, 6); index++)
        {
            var day = shift + random.Next(7);
            var from = random.Next(6, 20);
            entries.Add(CalendarEntry.Timed("Busy", At(day, from, random.Next(2) * 30), At(day, from + 1 + random.Next(2))));
        }

        return entries;
    }
}

internal static class PlannedSessionPerson
{
    public static string Person(this PlannedSession session) => string.Join("+", session.Participants);
}
