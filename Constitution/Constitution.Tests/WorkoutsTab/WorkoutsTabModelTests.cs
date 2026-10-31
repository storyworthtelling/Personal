using System.Text.RegularExpressions;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>C-5.4.1 (book p.12): today large, the next seven days small, with why anything changed. Invented data.</summary>
public class WorkoutsTabModelTests
{
    private static WorkoutsTabModel Model(WorkoutsSnapshot? snapshot = null, PendingSkip? pending = null) =>
        WorkoutsTabModel.Build(snapshot ?? Snapshot(), pending is null ? null : [pending]);

    private static DayClipping Row(WorkoutsTabModel model, DateOnly day) => model.Days.Single(item => item.Day == day);

    [Fact]
    public void TodayCarriesTheKitAndWhereThePagesAre()
    {
        var today = Model().Today!;

        Assert.True(today.IsToday);
        Assert.Equal("Today · 4:30 pm", today.Kicker);
        Assert.Equal("Strength, 40 minutes", today.Headline);
        Assert.Equal("5 exercises · dumbbells, bands, bench", today.KitLine);
        Assert.Equal("Score 132 bpm · Steady · on your tablet", today.ScoreLine);
    }

    [Fact]
    public void PagesNotSentYetAreSaidSo()
    {
        var snapshot = Snapshot() with { Facts = Facts().ToDictionary(pair => pair.Key, pair => pair.Value with { PagesOnTablet = false }) };

        Assert.Equal("Score 132 bpm · Steady · not on your tablet yet", Model(snapshot).Today!.ScoreLine);
    }

    [Fact]
    public void EachLaterDayShowsDayTimeLengthTypeAndScore()
    {
        var model = Model();
        var wed = Row(model, Wed);

        Assert.Equal(7, model.Days.Count);
        Assert.Equal("Wed", wed.DayLabel);
        Assert.Equal("6:30 am · 20 min · Mobility", wed.Headline);
        Assert.Equal("Score 118 bpm · Easy", wed.ScoreLine);
        Assert.Null(wed.ReasonLine);
        Assert.Equal("Treadmill", Row(model, Sat).KindWord);
    }

    [Fact]
    public void ChangedDaysCarryAReason()
    {
        var model = Model();

        Assert.Equal("Moved indoors: rain forecast", Row(model, Sat).ReasonLine);
        Assert.Equal("Sundays are gentle walks", Row(model, Sun).ReasonLine);
        Assert.Null(Row(model, Thu).ReasonLine);
    }

    [Fact]
    public void AFamilyWalkSaysWhoIsComing()
    {
        var walk = Row(Model(Snapshot(sundayTogether: Together.FamilyWalk)), Sun);

        Assert.Equal("2:00 pm · 45 min · Family walk", walk.Headline);
        Assert.Equal("Gentle · with everyone", walk.ScoreLine);
    }

    [Fact]
    public void ARestDayIsABareClipping()
    {
        var fri = Row(Model(), Fri);

        Assert.Equal(DayState.Rest, fri.State);
        Assert.Equal("Rest", fri.Headline);
        Assert.Null(fri.ScoreLine);
        Assert.Null(fri.ReasonLine);
        Assert.Null(fri.KitLine);
    }

    [Fact]
    public void ADayBeyondThePlanIsNotCalledARest()
    {
        var model = Model(Snapshot(nextWeek: false));

        Assert.Equal(DayState.NotPlanned, Row(model, Mon).State);
        Assert.Equal("Not planned yet", Row(model, Mon).Headline);
        Assert.False(Row(model, Mon).CanPress);
    }

    [Fact]
    public void ASkippedDayIsStruckThroughWithItsNote()
    {
        var ledger = SkipLedger.Empty.Settle(
            new SessionMarks("Sam", Thu, SessionKind.Strength, ["box"]),
            [new Constitution.Domain.Ink.InkResolution.Skipped("box")]);

        var thu = Row(Model(Snapshot(skips: ledger)), Thu);

        Assert.True(thu.IsSkipped);
        Assert.Equal("Skipped · counted as a rest", thu.SkippedNote);
        Assert.False(thu.CanPress);
    }

    [Fact]
    public void AWaitingSkipIsStruckThroughToo()
    {
        var pending = new PendingSkip("Sam", Thu, SessionKind.Strength, ThuStrength(), null, null, Now.AddSeconds(6));

        Assert.True(Row(Model(pending: pending), Thu).IsSkipped);
    }

    [Fact]
    public void NoHistoryNoWorkoutNow()
    {
        var model = Model();

        Assert.All(model.Days, day => Assert.True(day.Day > model.Today!.Day));
        var names = typeof(WorkoutsTabModel).GetMembers().Concat(typeof(DayClipping).GetMembers()).Select(member => member.Name);
        Assert.DoesNotContain(names, name => Regex.IsMatch(name, "History|Past|WorkoutNow|StartNow|Settings|Earlier"));
    }

    [Fact]
    public void NoPlanIsAPlainSentence()
    {
        var model = WorkoutsTabModel.NoPlan();

        Assert.False(model.HasPlan);
        Assert.Equal("No workouts are planned yet.", model.Status);
        Assert.Empty(model.Days);
    }

    [Fact]
    public void TodayAsARestDayIsBareToo()
    {
        var snapshot = Snapshot();
        var week = snapshot.Weeks[0];
        var bare = week with { Plan = week.Plan with { Plans = [week.Plan.Plans[0] with { Sessions = week.Plan.Plans[0].Sessions.Where(item => item.Day != Tue).ToList() }] } };
        var today = Model(snapshot with { Weeks = [bare, snapshot.Weeks[1]] }).Today!;

        Assert.Equal("Today", today.Kicker);
        Assert.Equal("Rest", today.Headline);
    }

    [Fact]
    public void TheStatusLineNamesTheWindow()
    {
        Assert.Equal("Today, then the next seven days", Model().Status);
    }
}
