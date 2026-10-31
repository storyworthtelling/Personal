using Constitution.Domain.Delivery;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.RecipeHour;

/// <summary>When a recipe is in its hour: from sixty minutes before the cook block until it begins, for the adult whose tablet this is. Monday's dinner is Sam's, block from 5:15 pm.</summary>
public class RecipeHourRuleTests
{
    private static readonly DateTimeOffset Opens = Edt(10, 26, 16, 15);

    private static DinnerChoice Monday() => WeekOf(Sunday).Dinners[1];

    private static string MondayName() => DeliveredName.For(DeliveryKind.Recipe, Monday().Day, Sam);

    private static IReadOnlyList<DateOnly> Due(
        DateTimeOffset now, string? person = Sam, MealWeekRecordRead? weeks = null, Func<string, DeliveredMealDocument?>? delivered = null) =>
        MealRecipeHour.DueWeeks(weeks ?? Kept(Sunday), now, person, delivered ?? (_ => null));

    // ---- the window ----

    [Fact]
    public void TheHourOpensSixtyMinutesBeforeTheBlock()
    {
        Assert.False(MealRecipeHour.InHour(Monday(), Opens - TimeSpan.FromTicks(1)));
        Assert.True(MealRecipeHour.InHour(Monday(), Opens));
    }

    [Fact]
    public void TheHourEndsWhenTheBlockBegins()
    {
        Assert.True(MealRecipeHour.InHour(Monday(), Edt(10, 26, 17, 15) - TimeSpan.FromTicks(1)));
        Assert.False(MealRecipeHour.InHour(Monday(), Edt(10, 26, 17, 15)));
    }

    [Fact]
    public void ADinnerNobodyCooksHasNoHour()
    {
        var leftovers = WeekOf(Sunday).Dinners[2];

        Assert.False(MealRecipeHour.InHour(leftovers, Opens));
    }

    // ---- which weeks are due ----

    [Fact]
    public void TheWeekInProgressIsDueInsideTheHour()
    {
        Assert.Equal([Sunday], Due(Opens));
        Assert.Equal([Sunday], Due(Edt(10, 26, 17, 10)));
    }

    [Fact]
    public void NothingIsDueBeforeOrAfterTheHour()
    {
        Assert.Empty(Due(Opens - TimeSpan.FromMinutes(1)));
        Assert.Empty(Due(Edt(10, 26, 17, 15)));
        Assert.Empty(Due(Edt(10, 27, 16, 30)));
    }

    [Fact]
    public void APageAlreadyOnTheTabletIsNotDueAgain()
    {
        var record = new DeliveredMealDocument(MondayName(), DeliveryKind.Recipe, Sam, Monday().Day, Monday().CookStart!.Value, "same");

        var due = Due(Opens, delivered: name => name == MondayName() ? record : null);

        Assert.Empty(due);
    }

    [Fact]
    public void AnotherPagesRecordDoesNotHideThisOne()
    {
        var other = new DeliveredMealDocument("something else", DeliveryKind.Recipe, Sam, Monday().Day, Monday().CookStart!.Value, "same");

        Assert.Equal([Sunday], Due(Opens, delivered: name => name == "something else" ? other : null));
    }

    [Fact]
    public void OnlyTheAdultWhoseTabletThisIsIsServed()
    {
        Assert.Empty(Due(Opens, person: Lee));
        Assert.Equal([Sunday], Due(Opens, person: " sam "));
    }

    [Fact]
    public void APhoneWithNoTabletHasNothingDue()
    {
        Assert.Empty(Due(Opens, person: null));
    }

    [Fact]
    public void WeeksThatCannotBeReadHaveNothingDue()
    {
        Assert.Empty(Due(Opens, weeks: new MealWeekRecordRead.NothingYet()));
        Assert.Empty(Due(Opens, weeks: new MealWeekRecordRead.Unreadable("unreadable")));
    }

    [Fact]
    public void TheRecordIsOnlyLookedAtWhenAnHourIsOpen()
    {
        var looks = 0;

        MealRecipeHour.DueWeeks(Kept(Sunday), Edt(10, 26, 9), Sam, _ =>
        {
            looks++;
            return null;
        });
        MealRecipeHour.DueWeeks(Kept(Sunday), Opens, Sam, _ =>
        {
            looks++;
            return null;
        });

        Assert.Equal(1, looks);
    }

    [Fact]
    public void OnlyTheWeekWithADinnerInTheHourIsDue()
    {
        var later = new StoredMealWeek(
            MealWeek.Planned(
                NextSunday,
                Enumerable.Range(0, 7).Select(offset => offset == 1
                    ? DinnerChoice.Cooked(NextSunday.AddDays(1), new RecipeId("lemon-rice"), Sam, Edt(11, 2, 17, 15), Edt(11, 2, 18), false, false, "From the box.")
                    : DinnerChoice.NothingPlanned(NextSunday.AddDays(offset), "Nothing is planned.")),
                []),
            Edt(10, 21, 12),
            []);
        var weeks = new MealWeekRecordRead.Held(new MealWeekRecord([Stored(Sunday), later]));

        Assert.Equal([Sunday], Due(Opens, weeks: weeks));
        Assert.Equal([NextSunday], Due(Edt(11, 2, 16, 20), weeks: weeks));
    }

    // ---- whose tablet ----

    [Fact]
    public void TheTabletBelongsToTheFirstAdultNamedInTheSetup()
    {
        Assert.Equal(Lee, MealRecipeHour.TabletPerson(Rules(), [Lee]));
        Assert.Equal(Sam, MealRecipeHour.TabletPerson(Rules(), ["sam", Lee]));
    }

    [Fact]
    public void NoOneIsNamedWhenNoAdultMatches()
    {
        Assert.Null(MealRecipeHour.TabletPerson(Rules(), []));
        Assert.Null(MealRecipeHour.TabletPerson(Rules(), ["Robin"]));
    }
}
