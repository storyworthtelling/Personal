using Constitution.Domain.Budgets;
using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using static Constitution.Tests.Calendar.InventedFamily;
using Docs = Constitution.Tests.MealWeekDocuments.DocsData;
using static Constitution.Tests.Templates.TemplateData;

namespace Constitution.Tests.Templates;

/// <summary>The sheet builder takes the rotation's meals; with none, pages 2 and 3 stay out as before.</summary>
public class BuilderTests
{
    internal static MealSheetBuildRequest Request(MealSheetDailyMeals? daily) =>
        new(Docs.Week(), Docs.Cards(), Zone, 4, Docs.Stocked(), MonthlyBudgets.None, new UnavailableHolidayCalendar())
        {
            History = Docs.Cards().Select(card => new Constitution.Domain.Meals.CookedMeal(card.Id, Sunday.AddDays(-14))).ToList(),
            Daily = daily,
        };

    [Fact]
    public void NoDailyMealsLeavesTheSheetAsItWas()
    {
        var model = MealSheetBuilder.Build(Request(null));

        Assert.Null(model.Daily);
        Assert.Equal(MealSheetBuilder.BasePages, model.Pages);
    }

    [Fact]
    public void AnEmptyLibraryGivesNoPages()
    {
        var picks = TemplateRotation.Pick(TemplateLibrary.Empty, Constitution.Tests.MealAnswers.MealAnswerData.Week, Facts);

        var model = MealSheetBuilder.Build(Request(picks.Daily));

        Assert.Null(model.Daily);
        Assert.Equal(MealSheetBuilder.BasePages, model.Pages);
        Assert.Equal(2, MealSheetDocument.Build(model).Count);
    }

    [Fact]
    public void TheRotationsMealsAreOnPagesTwoAndThree()
    {
        var picks = TemplateRotation.Pick(Full(), Constitution.Tests.MealAnswers.MealAnswerData.Week, Facts);

        var model = MealSheetBuilder.Build(Request(picks.Daily));

        Assert.Same(picks.Daily, model.Daily);
        Assert.Equal(MealSheetBuilder.BasePages + 2, model.Pages);
        Assert.Equal(4, MealSheetDocument.Build(model).Count);
    }
}
