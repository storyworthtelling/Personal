using Pulp.Domain.Calendar;
using Pulp.Domain.Core;

namespace Pulp.Tests.Calendar;

/// <summary>
/// D149: the nightly pass moves a changed date silently and never deletes-and-creates (a new event
/// would announce itself again); only events Pulp made (they carry its key) are ever deleted; D148:
/// timed where a time is known; D147: whose is on the title. Invented household: Sam and Alex.
/// </summary>
public class CalendarSyncPlanTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");

    private static string NameOf(ViewerId viewer) => viewer == Sam ? "Sam" : "Alex";

    private static CalendarEventDraft Draft(string key, DateOnly date, TimeOnly? time = null, string title = "Title") =>
        new(key, title, null, new CalendarWhen(date, time), TimeSpan.FromHours(1));

    private static ExistingEvent Existing(string id, string? key, DateOnly date, TimeOnly? time = null, string title = "Title") =>
        new(id, key, title, null, new CalendarWhen(date, time));

    [Fact]
    public void ANewKeyIsCreated()
    {
        var plan = CalendarSyncPlan.Compute([Draft("game:1", Today)], [], Today);
        Assert.Equal("game:1", Assert.IsType<CreateEvent>(Assert.Single(plan)).Draft.Key);
    }

    [Fact]
    public void AnUnchangedEventIsLeft()
    {
        var plan = CalendarSyncPlan.Compute([Draft("game:1", Today)], [Existing("e1", "game:1", Today)], Today);
        Assert.IsType<LeaveEvent>(Assert.Single(plan));
    }

    /// <summary>The bug this holds: delete-then-create on a moved date, which re-notifies everyone.</summary>
    [Fact]
    public void AMovedDateMovesTheExistingEventAndNeverDeletesOrCreates()
    {
        var plan = CalendarSyncPlan.Compute(
            [Draft("episode:show:1:4", Today.AddDays(5))],
            [Existing("e1", "episode:show:1:4", Today.AddDays(2))],
            Today);

        var move = Assert.IsType<MoveEvent>(Assert.Single(plan));
        Assert.Equal("e1", move.EventId);
        Assert.Equal(Today.AddDays(5), move.Draft.When.Date);
    }

    [Fact]
    public void ARescheduledGameTimeMovesTheEvent()
    {
        var plan = CalendarSyncPlan.Compute(
            [Draft("game:7", Today, new TimeOnly(20, 0))],
            [Existing("e1", "game:7", Today, new TimeOnly(19, 0))],
            Today);
        Assert.IsType<MoveEvent>(Assert.Single(plan));
    }

    [Fact]
    public void ATimedEventThatBecomesAllDayMoves()
    {
        var plan = CalendarSyncPlan.Compute(
            [Draft("game:7", Today)], [Existing("e1", "game:7", Today, new TimeOnly(19, 0))], Today);
        Assert.IsType<MoveEvent>(Assert.Single(plan));
    }

    [Fact]
    public void AKeyThatLeftThePlanIsDeletedWhenPulpMadeIt()
    {
        var plan = CalendarSyncPlan.Compute([], [Existing("e1", "game:9", Today.AddDays(3))], Today);
        Assert.Equal("e1", Assert.IsType<DeleteEvent>(Assert.Single(plan)).EventId);
    }

    /// <summary>The bug this holds: deleting what the owners wrote by hand.</summary>
    [Fact]
    public void AnEventWithoutPulpsKeyIsNeverTouched()
    {
        var plan = CalendarSyncPlan.Compute(
            [Draft("game:1", Today)],
            [Existing("hand1", null, Today.AddDays(3)), Existing("hand2", "", Today.AddDays(4)), Existing("hand3", "  ", Today)],
            Today);

        Assert.All(plan, operation => Assert.IsType<CreateEvent>(operation));
        Assert.DoesNotContain(plan, operation => operation is DeleteEvent or MoveEvent);
    }

    [Fact]
    public void APastEventIsKeptAsHistoryEvenWhenItLeftThePlan()
    {
        var plan = CalendarSyncPlan.Compute([], [Existing("e1", "game:9", Today.AddDays(-1))], Today);
        Assert.IsType<LeaveEvent>(Assert.Single(plan));
    }

    [Fact]
    public void ADuplicateOfOneKeyKeepsOneAndDeletesTheRest()
    {
        var plan = CalendarSyncPlan.Compute(
            [Draft("game:1", Today)],
            [Existing("b", "game:1", Today), Existing("a", "game:1", Today)],
            Today);

        Assert.Equal("a", Assert.IsType<LeaveEvent>(plan.Single(operation => operation is LeaveEvent)).EventId);
        Assert.Equal("b", Assert.IsType<DeleteEvent>(plan.Single(operation => operation is DeleteEvent)).EventId);
    }

    [Fact]
    public void TwoDraftsOfOneKeyAreABug()
    {
        Assert.Throws<ArgumentException>(() =>
            CalendarSyncPlan.Compute([Draft("game:1", Today), Draft("game:1", Today.AddDays(1))], [], Today));
    }

    /// <summary>
    /// Property: whatever the calendar holds and wants, the plan never creates a key that already has
    /// an event, never deletes an unkeyed event, never deletes a key still wanted, and after applying
    /// it every wanted key has exactly one event.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void PlansAreSafeAndConverge(int seed)
    {
        var random = new Random(seed);
        var keys = Enumerable.Range(0, 12).Select(number => "game:" + number).ToList();
        var wanted = keys.Where(_ => random.Next(2) == 0).Select(key => Draft(key, Today.AddDays(random.Next(14)), random.Next(2) == 0 ? new TimeOnly(random.Next(24), 0) : null)).ToList();
        var held = new List<ExistingEvent>();
        for (var number = 0; number < 20; number++)
        {
            var key = random.Next(4) == 0 ? null : keys[random.Next(keys.Count)];
            held.Add(Existing("ev" + number, key, Today.AddDays(random.Next(-3, 14)), random.Next(2) == 0 ? new TimeOnly(random.Next(24), 0) : null));
        }

        var plan = CalendarSyncPlan.Compute(wanted, held, Today);

        var heldKeys = held.Where(item => !string.IsNullOrWhiteSpace(item.Key)).Select(item => item.Key).ToHashSet();
        Assert.All(plan.OfType<CreateEvent>(), create => Assert.DoesNotContain(create.Draft.Key, heldKeys));
        var unkeyedIds = held.Where(item => string.IsNullOrWhiteSpace(item.Key)).Select(item => item.EventId).ToHashSet();
        var touched = plan.Select(OperationIds).Where(id => id is not null).ToList();
        Assert.DoesNotContain(touched, id => unkeyedIds.Contains(id!));
        var wantedKeys = wanted.Select(draft => draft.Key).ToHashSet();
        foreach (var delete in plan.OfType<DeleteEvent>())
        {
            var gone = held.Single(item => item.EventId == delete.EventId);
            Assert.True(!wantedKeys.Contains(gone.Key!) || held.Count(item => item.Key == gone.Key) > 1);
        }

        var deleted = plan.OfType<DeleteEvent>().Select(delete => delete.EventId).ToHashSet();
        foreach (var draft in wanted)
        {
            var surviving = held.Count(item => item.Key == draft.Key && !deleted.Contains(item.EventId));
            var created = plan.OfType<CreateEvent>().Count(create => create.Draft.Key == draft.Key);
            Assert.Equal(1, surviving + created);
        }
    }

    private static string? OperationIds(CalendarOperation operation) => operation switch
    {
        MoveEvent move => move.EventId,
        DeleteEvent delete => delete.EventId,
        LeaveEvent leave => leave.EventId,
        _ => null,
    };

    // ---- drafts from items ----

    private static CalendarItem Item(CalendarItemKind kind, string key, string name, CalendarWhen when, params ViewerId[] whose) =>
        new(key, kind, when, name, null, null, null, null, whose, []);

    [Fact]
    public void AnEpisodeTitleCarriesWhoseItIs()
    {
        var draft = CalendarDrafts.From(
            Item(CalendarItemKind.NewEpisode, "episode:harbor:1:4", "Harbor Lights", CalendarWhen.AllDay(Today), Sam), NameOf);
        Assert.Equal("New episode — Harbor Lights (Sam)", draft.Title);
    }

    [Fact]
    public void ASharedItemNamesEveryone()
    {
        var draft = CalendarDrafts.From(
            Item(CalendarItemKind.Release, "release:x", "Quiet Orchard", CalendarWhen.AllDay(Today), Sam, Alex), NameOf);
        Assert.Contains("Sam", draft.Title);
        Assert.Contains("Alex", draft.Title);
    }

    [Fact]
    public void ADraftKeepsTheItemsKeyAndItsTimedness()
    {
        var timed = CalendarDrafts.From(
            Item(CalendarItemKind.Game, "game:g1", "Rovers v Harriers", new CalendarWhen(Today, new TimeOnly(19, 30)), Sam), NameOf);
        var allDay = CalendarDrafts.From(
            Item(CalendarItemKind.NewEpisode, "episode:a:1:1", "A", CalendarWhen.AllDay(Today), Sam), NameOf);

        Assert.Equal("game:g1", timed.Key);
        Assert.False(timed.When.IsAllDay);
        Assert.True(timed.Length > TimeSpan.Zero);
        Assert.True(allDay.When.IsAllDay);
    }

    [Fact]
    public void TheKeyNeverContainsTheDate()
    {
        var item = Item(CalendarItemKind.Game, "game:g1", "Rovers v Harriers", new CalendarWhen(Today, new TimeOnly(19, 30)), Sam);
        var moved = item with { When = new CalendarWhen(Today.AddDays(3), new TimeOnly(20, 0)) };
        Assert.Equal(CalendarDrafts.From(item, NameOf).Key, CalendarDrafts.From(moved, NameOf).Key);
    }

    [Fact]
    public void DeletesComeFirstThenMovesThenCreates()
    {
        // Bug kept out: creates ran first and a failing one stopped the write before the delete that takes a
        // spoiler off the shared calendar.
        var plan = CalendarSyncPlan.Compute(
            [Draft("game:new", Today), Draft("game:moved", Today.AddDays(5)), Draft("game:same", Today)],
            [Existing("e-moved", "game:moved", Today.AddDays(2)), Existing("e-same", "game:same", Today), Existing("e-gone", "game:gone", Today.AddDays(3))],
            Today);

        Assert.IsType<DeleteEvent>(plan[0]);
        Assert.IsType<MoveEvent>(plan[1]);
        Assert.IsType<CreateEvent>(plan[2]);
        Assert.IsType<LeaveEvent>(plan[3]);
    }

    [Fact]
    public void ADuplicateEventIsDeletedBeforeTheKeptOneMoves()
    {
        var plan = CalendarSyncPlan.Compute(
            [Draft("game:1", Today.AddDays(5))],
            [Existing("e1", "game:1", Today), Existing("e2", "game:1", Today)],
            Today);

        Assert.IsType<DeleteEvent>(plan[0]);
        Assert.IsType<MoveEvent>(plan[1]);
    }

    [Fact]
    public void ASubSecondDifferenceInATimeOfDayIsNotAMove()
    {
        // Bug kept out: a feed time with fractions of a second never equals the calendar's whole-second
        // reading, so the event was patched every night.
        var plan = CalendarSyncPlan.Compute(
            [Draft("game:7", Today, new TimeOnly(19, 0, 0, 450))],
            [Existing("e1", "game:7", Today, new TimeOnly(19, 0))],
            Today);
        Assert.IsType<LeaveEvent>(Assert.Single(plan));
    }

    [Fact]
    public void ADifferenceOfAWholeSecondIsStillAMove()
    {
        var plan = CalendarSyncPlan.Compute(
            [Draft("game:7", Today, new TimeOnly(19, 0, 1))],
            [Existing("e1", "game:7", Today, new TimeOnly(19, 0))],
            Today);
        Assert.IsType<MoveEvent>(Assert.Single(plan));
    }
}
