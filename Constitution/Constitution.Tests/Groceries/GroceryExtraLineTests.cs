using Constitution.Domain.Groceries;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Tests.MealSheet;
using static Constitution.Tests.Groceries.GroceryData;

namespace Constitution.Tests.Groceries;

/// <summary>D111: a denied evening is one plain line on the list ("Tuesday dinner"), after the aisles, never scaled, never an ingredient. Invented data.</summary>
public class GroceryExtraLineTests
{
    private static GroceryList With(params string[] extra) => GroceryList.From(ThreeDinners(), Cards, Pantry.Empty, 4, extra);

    [Fact]
    public void WithoutExtraLinesTheListIsAsItWas()
    {
        var plain = GroceryList.From(ThreeDinners(), Cards, Pantry.Empty, 4);

        Assert.Empty(plain.ExtraLines);
        Assert.Equal(plain.Lines, With().Lines);
        Assert.Equal(plain.Sections.Count, With("Tuesday dinner").Sections.Count);
    }

    [Fact]
    public void AnExtraLineIsItsOwnLineAndNeverAnAisleLine()
    {
        var list = With("Tuesday dinner", "Friday dinner");

        Assert.Equal(["Tuesday dinner", "Friday dinner"], list.ExtraLines);
        Assert.DoesNotContain(list.Lines, line => line.Name.Contains("dinner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtraLinesAreMergedByTextAndNeverScaled()
    {
        var small = GroceryList.From(ThreeDinners(), Cards, Pantry.Empty, 2, [" Tuesday dinner", "tuesday dinner ", "Friday dinner"]);
        var large = GroceryList.From(ThreeDinners(), Cards, Pantry.Empty, 8, ["Tuesday dinner", "Friday dinner"]);

        Assert.Equal(["Tuesday dinner", "Friday dinner"], small.ExtraLines);
        Assert.Equal(small.ExtraLines, large.ExtraLines);
    }

    [Fact]
    public void ABlankExtraLineIsLeftOut() =>
        Assert.Equal(["Friday dinner"], With("  ", "", "Friday dinner").ExtraLines);

    [Fact]
    public void AListOfOnlyExtraLinesIsNotEmpty()
    {
        var list = GroceryList.From(MealPlacementData(), Cards, Pantry.Empty, 4, ["Tuesday dinner"]);

        Assert.False(list.IsEmpty);
        Assert.Empty(list.Sections);
        Assert.Equal(["Tuesday dinner"], list.ExtraLines);
    }

    [Fact]
    public void AListWithNothingAtAllIsStillEmpty() =>
        Assert.True(GroceryList.From(MealPlacementData(), Cards, Pantry.Empty, 4, []).IsEmpty);

    [Fact]
    public void KeepGetsTheExtraLinesAfterTheAislesUnderTheirOwnHeader()
    {
        var body = GroceryShare.Plan(With("Tuesday dinner", "Friday dinner"), keepIsInstalled: true).Body.Split('\n');

        var header = Array.IndexOf(body, GroceryList.ExtraHeading);
        Assert.True(header > 0);
        Assert.Equal(["Tuesday dinner", "Friday dinner"], body[(header + 1)..]);
        Assert.Equal(string.Empty, body[header - 1]);
        Assert.Contains(Aisles.Label(Aisle.Produce), body[..header]);
    }

    [Fact]
    public void KeepSendsAListOfOnlyExtraLines()
    {
        var decision = GroceryShare.Plan(GroceryList.From(MealPlacementData(), Cards, Pantry.Empty, 4, ["Tuesday dinner"]), keepIsInstalled: true);

        Assert.Equal(GroceryShareOutcome.Send, decision.Outcome);
        Assert.Equal(GroceryList.ExtraHeading + "\nTuesday dinner", decision.Body);
    }

    [Fact]
    public void TheSheetsGroceryPagePrintsEachExtraLineOnItsOwnLineAfterTheAisles()
    {
        var list = With("Tuesday dinner", "Friday dinner");
        var page = MealSheetGrocerySamples.Page(MealSheetGrocerySamples.Model(MealSheetGrocerySamples.Groceries(list: list)));

        var other = page.Printed.Where(item => item.Name.StartsWith("list-extra-", StringComparison.Ordinal)).OrderBy(item => item.Bounds.Top).ToList();
        Assert.Equal(["Tuesday dinner", "Friday dinner"], other.Select(item => item.Text));
        var heading = page.Printed.Single(item => item.Name == "extra-heading");
        Assert.Equal(GroceryList.ExtraHeading.ToUpperInvariant(), heading.Text);
        var lastAisle = page.Printed.Where(item => item.Name.StartsWith("list-", StringComparison.Ordinal) && !item.Name.StartsWith("list-extra-", StringComparison.Ordinal) && item.Name != "list-heading" && item.Name != "list-note").Max(item => item.Bounds.Top);
        Assert.True(heading.Bounds.Top > lastAisle);
    }

    [Fact]
    public void TheBuilderHandsTheExtraLinesToTheList()
    {
        var model = MealSheetBuilder.Build(new MealSheetBuildRequest(
            ThreeDinners(), Cards, Constitution.Tests.Calendar.InventedFamily.Zone, 4, Pantry.Empty,
            Constitution.Domain.Budgets.MonthlyBudgets.None, new UnavailableHolidayCalendar())
        {
            ExtraLines = ["Tuesday dinner"],
        });

        Assert.Equal(["Tuesday dinner"], model.Groceries!.List.ExtraLines);
    }

    private static Constitution.Domain.Meals.MealWeek MealPlacementData() => Constitution.Tests.MealPlacement.MealPlacementData.WeekOf();
}
