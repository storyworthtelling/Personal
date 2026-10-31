using Constitution.Domain.Groceries;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlacement.MealPlacementData;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.CrossRun;

/// <summary>
/// Which run a stored key belongs to. The family decides what each run may treat as "my own earlier event, drop it" and what is
/// somebody's time and must be gone round. Every key either run writes must land in its own family, whatever way it was made, and
/// anything the app cannot name is <see cref="KeyFamily.Unknown"/>: never a family, so never dropped.
/// </summary>
public class KeyFamilyTests
{
    [Fact]
    public void EverySessionKindMakesAWorkoutKey()
    {
        foreach (var kind in Enum.GetValues<SessionKind>())
        {
            Assert.Equal(KeyFamily.Workout, KeyFamilies.Of(PlacementKey.ForWorkout("sam", kind, 5, 0)));
            Assert.Equal(KeyFamily.Workout, KeyFamilies.Of(PlacementKey.ForWorkout("lee", kind, 120, 3).Value));
        }
    }

    [Fact]
    public void EveryMealRoleOnEveryEveningMakesAMealKey()
    {
        foreach (var role in Enum.GetValues<MealEventRole>())
        {
            for (var offset = 0; offset < 7; offset++)
            {
                var key = MealCalendar.KeyOf(role, "Sam", Sunday, Sunday.AddDays(offset));
                Assert.True(KeyFamilies.Of(key) == KeyFamily.HouseholdMeal, $"{role} on evening {offset} gave {key.Value}");
            }
        }
    }

    [Fact]
    public void TheGroceriesTripIsAMealKey()
    {
        var key = PlacementKey.ForHousehold("Lee", Week, GroceryPlacement.Identity);

        Assert.Equal(KeyFamily.HouseholdMeal, KeyFamilies.Of(key));
    }

    [Fact]
    public void EngineKeysLandInTheirFamilies()
    {
        var week = WeekOf(
            CookedOn(1, Chili, "Sam", 45),
            CookedOn(2, Stew, "Lee", 120),
            CookedOn(4, Pasta, "Sam", 30));
        var options = new MealPlacementOptions { SlowCookerCards = new HashSet<Constitution.Domain.Recipes.RecipeId> { Stew.Id } };
        var people = PeopleFor(House, Calendars());
        var trip = GroceryPlacement.Request(Sunday, House, people, Edt(10, 22, 8));
        var meals = MealCalendar.Requests(week, House, Cards, options).ToList();
        meals.Add(trip!);
        var workouts = PlanOfSessions([Workout(1, (9, 0), (11, 0), (9, 0), 30, "Sam"), Workout(3, (9, 0), (11, 0), (9, 0), 30, "Lee")]);

        var mealKeys = PlacementEngine.Wanted(new PlacementRequest(Sunday, people, meals, null)).Select(item => item.Key).ToList();
        var workoutKeys = PlacementEngine.Wanted(new PlacementRequest(Sunday, people, [], workouts)).Select(item => item.Key).ToList();

        Assert.True(mealKeys.Count >= 5, "Three cook blocks, a morning start and the trip were asked for.");
        Assert.All(mealKeys, key => Assert.Equal(KeyFamily.HouseholdMeal, KeyFamilies.Of(key)));
        Assert.Equal(2, workoutKeys.Count);
        Assert.All(workoutKeys, key => Assert.Equal(KeyFamily.Workout, KeyFamilies.Of(key)));
    }

    [Theory]
    [InlineData("c1:sam:strength:5:0", KeyFamily.Workout)]
    [InlineData("c1:lee:outdoorcardio:12:3", KeyFamily.Workout)]
    [InlineData("c1:sam:treadmill:5:1", KeyFamily.Workout)]
    [InlineData("c1:sam:mobility:5:1", KeyFamily.Workout)]
    [InlineData("c1:sam:gentlewalk:5:1", KeyFamily.Workout)]
    [InlineData("c1:sam:household:5:cook-tue", KeyFamily.HouseholdMeal)]
    [InlineData("c1:lee:household:12:slowcooker-sat", KeyFamily.HouseholdMeal)]
    [InlineData("c1:sam:household:5:groceries", KeyFamily.HouseholdMeal)]
    [InlineData(" c1:sam:household:5:cook-sun ", KeyFamily.HouseholdMeal)]
    public void TheFrozenSpellingsAreTheirFamilies(string stored, KeyFamily family)
    {
        Assert.Equal(family, KeyFamilies.Of(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("c2:sam:household:5:cook-tue")]
    [InlineData("C1:sam:household:5:cook-tue")]
    [InlineData("c1:sam:household:5")]
    [InlineData("c1:sam:household:5:cook-tue:extra")]
    [InlineData("c1:sam:household:x:cook-tue")]
    [InlineData("c1:sam:household:5:cook")]
    [InlineData("c1:sam:household:5:cook-someday")]
    [InlineData("c1:sam:household:5:slowcooker-")]
    [InlineData("c1:sam:household:5:groceries-mon")]
    [InlineData("c1:sam:household:5:errand")]
    [InlineData("c1:sam:strength:x:0")]
    [InlineData("c1:sam:strength:5:y")]
    [InlineData("c1:sam:swimming:5:0")]
    [InlineData("c1::strength:5:0")]
    public void AnythingElseIsUnknownAndSoNeverDropped(string? stored)
    {
        Assert.Equal(KeyFamily.Unknown, KeyFamilies.Of(stored));
    }

    [Fact]
    public void OtherHouseholdEventsAreUnknown()
    {
        Assert.Equal(KeyFamily.Unknown, KeyFamilies.Of(PlacementKey.ForHousehold("Sam", Week, "dentist")));
        Assert.Equal(KeyFamily.Unknown, KeyFamilies.Of(PlacementKey.ForHousehold("Sam", Week, "dinner-a")));
    }

    [Fact]
    public void ANameCannotForgeAFamily()
    {
        // The separator is percent-encoded inside a part, so a person named like a key's tail is still just a person.
        var forged = PlacementKey.ForHousehold("x:strength:5:0", Week, "errand");

        Assert.Equal(KeyFamily.Unknown, KeyFamilies.Of(forged));
    }

    [Fact]
    public void ADefaultKeyIsUnknown()
    {
        Assert.Equal(KeyFamily.Unknown, KeyFamilies.Of(default(PlacementKey)));
    }

    [Fact]
    public void TheWeekdayWordsAreTheOnesTheMealKeysUse()
    {
        // The words in a meal key are frozen in MealPlacement; this file repeats them, so a change to one is a red test here.
        var words = Enumerable.Range(0, 7)
            .Select(offset => MealCalendar.KeyOf(MealEventRole.Cook, "Sam", Sunday, Sunday.AddDays(offset)).Value.Split(':')[4])
            .ToList();

        Assert.Equal(["cook-sun", "cook-mon", "cook-tue", "cook-wed", "cook-thu", "cook-fri", "cook-sat"], words);
    }
}
