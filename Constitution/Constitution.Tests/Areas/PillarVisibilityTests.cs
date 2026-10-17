using Constitution.Domain.Areas;

namespace Constitution.Tests.Areas;

/// <summary>
/// Pillars are switchable per install; Today and Settings are always shown (brief, book p.17).
/// </summary>
public class PillarVisibilityTests
{
    private static readonly Pillars AllOn = new(Body: true, Workouts: true, Meals: true);

    [Fact]
    public void TheFiveAreasAreInTheApprovedOrderWithLiteralIds()
    {
        Assert.Equal(["today", "workouts", "meals", "body", "settings"], ConstitutionAreas.All.Select(area => area.Id));
        Assert.Equal(["Today", "Workouts", "Meals", "Body", "Settings"], ConstitutionAreas.All.Select(area => area.Name));
    }

    [Fact]
    public void AllPillarsOnShowsAllFive()
    {
        Assert.Equal(5, ConstitutionAreas.Visible(AllOn).Count);
    }

    [Theory]
    [InlineData(false, true, true, "body")]
    [InlineData(true, false, true, "workouts")]
    [InlineData(true, true, false, "meals")]
    public void OffHidesTheTab(bool body, bool workouts, bool meals, string hidden)
    {
        var visible = ConstitutionAreas.Visible(new Pillars(body, workouts, meals)).Select(area => area.Id).ToArray();

        Assert.DoesNotContain(hidden, visible);
        Assert.Equal(4, visible.Length);
    }

    [Fact]
    public void TheStripClosesUpAndKeepsOrder()
    {
        var visible = ConstitutionAreas.Visible(new Pillars(Body: false, Workouts: true, Meals: false));

        Assert.Equal(["today", "workouts", "settings"], visible.Select(area => area.Id));
    }

    [Fact]
    public void TodayAndSettingsAreAlwaysShown()
    {
        var visible = ConstitutionAreas.Visible(new Pillars(false, false, false)).Select(area => area.Id);

        Assert.Equal(["today", "settings"], visible);
    }

    /// <summary>
    /// The household plan is a separate fact. Switching Meals off on one install changes that
    /// install's tabs only; the plan, and the other install's tabs, are untouched (book p.17).
    /// </summary>
    [Fact]
    public void MealsStaysAHouseholdPlan()
    {
        var plan = new HouseholdPlan(MealsPlanned: true);
        var thisInstall = AllOn;
        var otherInstall = AllOn;

        var switchedOff = thisInstall with { Meals = false };

        Assert.DoesNotContain("meals", ConstitutionAreas.Visible(switchedOff).Select(area => area.Id));
        Assert.Contains("meals", ConstitutionAreas.Visible(otherInstall).Select(area => area.Id));
        Assert.True(plan.MealsPlanned);
        Assert.Equal(new HouseholdPlan(MealsPlanned: true), plan);
        Assert.DoesNotContain(typeof(Pillars).GetProperties(), property => property.PropertyType == typeof(HouseholdPlan));
    }
}
