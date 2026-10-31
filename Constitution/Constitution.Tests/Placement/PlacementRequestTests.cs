using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Placement;

/// <summary>
/// What a caller can get wrong, said plainly. Each of these is a programming mistake rather than a fact
/// about a calendar, so each is refused loudly instead of being placed somehow.
/// </summary>
public class PlacementRequestTests
{
    private static readonly PlacementWindow Afternoon = new(At(1, 16), At(1, 17));

    private static HouseholdEventRequest Event(
        string identity = "dinner-a",
        string title = "Cook: chili",
        int minutes = 30,
        IReadOnlyList<string>? people = null,
        IReadOnlyList<PlacementWindow>? windows = null) =>
        new(identity, title, TimeSpan.FromMinutes(minutes), people ?? [Sam], windows ?? [Afternoon]);

    // ---- the event itself ----

    [Fact]
    public void AnEventAsksForWhatItNeeds()
    {
        var made = Event(title: "  Cook: chili ", people: [" Sam ", "Lee"]);

        Assert.Equal("Cook: chili", made.Title);
        Assert.Equal([Sam, Lee], made.People);
    }

    [Theory]
    [InlineData(" ", "Cook", 30)]
    [InlineData("dinner-a", " ", 30)]
    [InlineData("dinner-a", "Cook", 0)]
    [InlineData("dinner-a", "Cook", -5)]
    [InlineData("dinner-2026-11-03", "Cook", 30)]
    public void AnEventNeedsAnIdentityATitleAndSomeTime(string identity, string title, int minutes)
    {
        Assert.Throws<ArgumentException>(() => Event(identity, title, minutes));
    }

    [Fact]
    public void AnEventNeedsPeopleAndWindows()
    {
        Assert.Throws<ArgumentException>(() => Event(people: []));
        Assert.Throws<ArgumentException>(() => Event(people: [Sam, " "]));
        Assert.Throws<ArgumentException>(() => Event(people: [Sam, "sam"]));
        Assert.Throws<ArgumentException>(() => Event(windows: []));
    }

    [Fact]
    public void AWindowRunsForward()
    {
        Assert.Throws<ArgumentException>(() => new PlacementWindow(At(1, 17), At(1, 16)));
        Assert.Throws<ArgumentException>(() => new PlacementWindow(At(1, 16), At(1, 16)));
    }

    [Fact]
    public void TheShowerIsNotNegative()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PlacementOptions { ShowerBuffer = TimeSpan.FromMinutes(-1) });
        Assert.Equal(TimeSpan.Zero, new PlacementOptions { ShowerBuffer = TimeSpan.Zero }.ShowerBuffer);
    }

    // ---- the request as a whole ----

    [Fact]
    public void ANobodyInTheHouseholdIsRefused()
    {
        var request = PlaceRequest(Calendars(), null, Event(people: ["Robin"]));

        var error = Assert.Throws<ArgumentException>(() => PlacementEngine.Plan(request, Fresh));

        Assert.Contains("Robin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASessionForSomeoneWithNoCalendarIsRefused()
    {
        var request = PlaceRequest(Calendars(), PlanOf(Planned(1, (16, 0), (18, 0), Effort(40), "Robin")));

        var error = Assert.Throws<ArgumentException>(() => PlacementEngine.Plan(request, Fresh));

        Assert.Contains("Robin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoEventsWithOneIdentityAreRefused()
    {
        var request = PlaceRequest(Calendars(), null, Event("dinner-a", "Cook: chili"), Event("dinner-a", "Cook: stew"));

        var error = Assert.Throws<ArgumentException>(() => PlacementEngine.Plan(request, Fresh));

        Assert.Contains("dinner-a", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoEventsWithOneIdentityForDifferentPeopleAreFine()
    {
        var request = PlaceRequest(Calendars(), null, Event("dinner-a", "Cook: chili", people: [Sam]), Event("dinner-a", "Cook: stew", people: [Lee]));

        Assert.Equal(2, PlacementEngine.Plan(request, Fresh).ToCreate.Count);
    }

    [Fact]
    public void ThePeopleAreNamedOnce()
    {
        var request = PlaceRequest(Calendars());
        var twice = request with { People = [request.People[0], request.People[0]] };

        Assert.Throws<ArgumentException>(() => PlacementEngine.Plan(twice, Fresh));
    }

    [Fact]
    public void ThePlanIsForTheWeekAskedAbout()
    {
        var request = PlaceRequest(Calendars(), PlanOf(Planned(1, (16, 0), (18, 0), Effort(40), Sam)));
        var nextWeek = request with { WeekStart = WeekStart.AddDays(7) };

        Assert.Throws<ArgumentException>(() => PlacementEngine.Plan(nextWeek, Fresh));
    }

    [Fact]
    public void NothingToPlaceIsAnEmptyPlan()
    {
        var plan = PlacementEngine.Plan(PlaceRequest(Calendars()), Fresh);

        Assert.Empty(plan.Outcomes);
        Assert.Empty(PlacementEngine.Wanted(PlaceRequest(Calendars())));
    }

    [Fact]
    public void ARequestNamesWhatItWantsBeforeAnythingIsPlaced()
    {
        var request = PlaceRequest(
            Calendars(),
            PlanOf(Planned(1, (16, 0), (18, 0), Effort(40), Sam)),
            Event("dinner-a", "Cook: chili", people: [Lee]));

        var wanted = PlacementEngine.Wanted(request);

        Assert.Equal(2, wanted.Count);
        Assert.Contains(wanted, item => item.Key == StrengthKey(Sam) && item.Person == Sam && item.Title == "Workout · 40 min");
        Assert.Contains(wanted, item => item.Key == HouseholdKey(Lee, "dinner-a") && item.Person == Lee && item.Title == "Cook: chili");
    }

    [Fact]
    public void ANullRequestIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => PlacementEngine.Plan(null!, Fresh));
        Assert.Throws<ArgumentNullException>(() => PlacementEngine.Plan(PlaceRequest(Calendars()), null!));
        Assert.Throws<ArgumentNullException>(() => PlacementEngine.Wanted(null!));
    }
}
