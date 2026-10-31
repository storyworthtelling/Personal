using Constitution.Domain.Delivery;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealWeekDocuments;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.RecipeHour;

/// <summary>The documents step with the sheet left out: the same recipes, the same rules about the hour, and nothing about the pantry or the budget.</summary>
public class RecipesOnlyStepTests
{
    private static readonly DateTimeOffset Inside = Edt(10, 25, 16, 20);

    [Fact]
    public async Task OnlyRecipesAreMade()
    {
        var rig = new DocsRig { Now = Inside };

        var done = await rig.RunAsync(rig.Request().WithRecipesOnly());

        Assert.NotEmpty(done.Documents);
        Assert.All(done.Documents, item => Assert.Equal(DeliveryKind.Recipe, item.Kind));
        Assert.DoesNotContain(rig.Tablet.Titles.Values, title => title.Contains("Meals", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePantryIsNotNeeded()
    {
        var rig = new DocsRig { Now = Inside };
        rig.Pantry.Answer = new MealPantryRead.Unreadable("The pantry cannot be opened.");

        var done = await rig.RunAsync(rig.Request().WithRecipesOnly());

        Assert.Null(done.PantrySentence);
        Assert.Equal(MealDocumentVerdict.Delivered, rig.RecipeOn(done, 0).Verdict);
    }

    [Fact]
    public async Task WithoutTheFlagTheSheetIsStillJudged()
    {
        var rig = new DocsRig { Now = Inside };

        var done = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.TooLate, rig.Sheet(done).Verdict);
        Assert.Contains(done.Documents, item => item.Kind == DeliveryKind.Meals);
    }

    [Fact]
    public async Task TheHourStillGovernsEachRecipe()
    {
        var rig = new DocsRig { Now = Inside };

        var done = await rig.RunAsync(rig.Request().WithRecipesOnly());

        Assert.Equal(MealDocumentVerdict.Delivered, rig.RecipeOn(done, 0).Verdict);
        Assert.Equal(MealDocumentVerdict.NotYet, rig.RecipeOn(done, 2).Verdict);
    }

    [Fact]
    public async Task TheRosterIsStillNeeded()
    {
        var rig = new DocsRig { Now = Inside };
        rig.Roster.Answer = new MealRosterRead.Unavailable("The household file cannot be opened.");

        var result = await MealDocumentsStep.RunAsync(rig.Request().WithRecipesOnly(), rig.Seams, CancellationToken.None);

        Assert.Equal(MealDocumentsStoppedBecause.RosterUnavailable, Assert.IsType<MealDocumentsResult.Stopped>(result).Because);
    }
}

internal static class RecipesOnlyExtensions
{
    public static MealDocumentRequest WithRecipesOnly(this MealDocumentRequest request) => request with { RecipesOnly = true };
}
