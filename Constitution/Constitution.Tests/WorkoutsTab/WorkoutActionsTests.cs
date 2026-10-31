using System.Text.RegularExpressions;
using Constitution.Domain.Workouts.Screens;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>C-5.4.2: a long-press on a day raises the card with exactly its actions (book p.12). Invented data.</summary>
public class WorkoutActionsTests
{
    private static DayClipping Day(DateOnly day) => WorkoutsTabModel.Build(Snapshot(), null).Days.Single(item => item.Day == day);

    [Fact]
    public void ExactlyTwoActions()
    {
        var card = ActionCardModel.For(Day(Thu));

        Assert.Equal("Thursday · Strength · 40 min", card.Title);
        Assert.Equal("4:00 pm · on your calendar", card.Subtitle);
        Assert.Equal(["Skip this day", "Swap with…"], card.Actions.Select(action => action.Label));
        Assert.Equal([WorkoutActionId.SkipThisDay, WorkoutActionId.SwapWith], card.Actions.Select(action => action.Id));
    }

    [Fact]
    public void ASessionWithNoEventSaysNothingOfTheCalendar()
    {
        Assert.Equal("6:30 am", ActionCardModel.For(Day(Wed)).Subtitle);
    }

    [Fact]
    public void ARestDayOffersOnlyMoveAWorkoutHere()
    {
        var card = ActionCardModel.For(Day(Fri));

        Assert.Equal("Friday · Rest", card.Title);
        Assert.Equal(["Move a workout here…"], card.Actions.Select(action => action.Label));
        Assert.Equal([WorkoutActionId.MoveWorkoutHere], card.Actions.Select(action => action.Id));
    }

    [Fact]
    public void ADayThatCannotBePressedHasNoCard()
    {
        var model = WorkoutsTabModel.Build(Snapshot(nextWeek: false), null);

        Assert.Throws<InvalidOperationException>(() => ActionCardModel.For(model.Days.Single(item => item.Day == Mon)));
    }

    [Fact]
    public void NoActionIsAConfirmation()
    {
        var names = Enum.GetNames<WorkoutActionId>();

        Assert.DoesNotContain(names, name => Regex.IsMatch(name, "Confirm|AreYouSure|Cancel|Yes|No$"));
    }
}
