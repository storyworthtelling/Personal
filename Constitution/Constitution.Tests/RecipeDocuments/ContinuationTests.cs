using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Constitution.Tests.RecipeDocuments;

/// <summary>A page one that does not fit flows to a continuation page rather than throwing.</summary>
public class ContinuationTests
{
    private static List<RecipeIngredient> Many(int count, string group = "Main") =>
        Enumerable.Range(1, count).Select(i => new RecipeIngredient(i <= count / 2 ? group : "Then", "1 tsp", $"spice number {i}")).ToList();

    private static IEnumerable<string> Texts(InkPageLayout page, PrintedRole role) =>
        page.Printed.Where(i => i.Role == role && i.Text is not null).Select(i => i.Text!);

    [Fact]
    public void ManyIngredientsFlowToAContinuationPage()
    {
        var pages = RecipeDocument.Build(RecipeSamples.Chicken(ingredients: Many(20)));

        Assert.True(pages.Count >= 2);
        Assert.Equal(Enumerable.Range(1, pages.Count).Select(n => $"{n} of {pages.Count + 3}"), pages.Select(p => p.Printed.Single(i => i.Name == "page-number").Text));
        Assert.All(pages, p => Assert.Empty(p.Violations()));
    }

    [Fact]
    public void EveryIngredientIsPrintedOnceInOrder()
    {
        var pages = RecipeDocument.Build(RecipeSamples.Chicken(ingredients: Many(40)));
        var names = pages.SelectMany(p => p.Printed).Where(i => i.Name.StartsWith("ingredient-", StringComparison.Ordinal)).Select(i => i.Text!).ToList();

        Assert.Equal(Enumerable.Range(1, 40).Select(i => $"spice number {i}"), names);
    }

    [Fact]
    public void TheHeadcountAndTheEquipmentAreOnceEach()
    {
        var pages = RecipeDocument.Build(RecipeSamples.Chicken(ingredients: Many(30)));
        var all = pages.SelectMany(p => p.Printed).ToList();

        Assert.Single(all, i => i.Role == PrintedRole.Strong);
        Assert.Single(all, i => i.Name == "equipment-label");
        Assert.Single(all, i => i.Name == "equipment-rule");
        Assert.Equal(["·  Large rimmed sheet pan", "·  Chef’s knife and board", "·  Large bowl", "·  Instant-read thermometer"], all.Where(i => i.Text?.StartsWith('·') == true).Select(i => i.Text));
        Assert.Single(all, i => i.Text?.StartsWith("Scaled from", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ARowIsNeverSplitAndNothingReachesTheFooter()
    {
        foreach (var count in new[] { 10, 11, 12, 13, 17, 25, 33 })
        {
            foreach (var page in RecipeDocument.Build(RecipeSamples.Chicken(ingredients: Many(count))))
            {
                var footer = page.Printed.Single(i => i.Name == "footer").Bounds;
                Assert.All(page.Printed.Where(i => i.Name is not ("footer" or "page-number" or "kicker") && !i.Name.StartsWith("notes", StringComparison.Ordinal) && i.Role != PrintedRole.Rule),
                    i => Assert.True(i.Bounds.Bottom < footer.Top, $"{count} ingredients: '{i.Text}' reaches {i.Bounds.Bottom:0}."));
            }
        }
    }

    [Fact]
    public void ACarriedOverGroupSaysSo()
    {
        var pages = RecipeDocument.Build(RecipeSamples.Chicken(ingredients: Enumerable.Range(1, 24).Select(i => new RecipeIngredient("Everything", "1 tsp", $"spice number {i}")).ToList()));

        Assert.Contains("EVERYTHING", Texts(pages[0], PrintedRole.Label));
        Assert.Contains("EVERYTHING (CONTINUED)", Texts(pages[1], PrintedRole.Label));
        Assert.Contains("RECIPE · INGREDIENTS, CONTINUED", Texts(pages[1], PrintedRole.Label));
    }

    [Fact]
    public void TheChildrensPageNumberFollowsTheExtraPages()
    {
        var withOne = RecipeDocument.Build(RecipeSamples.Chicken(ingredients: Many(3)));
        var flowed = RecipeDocument.Build(RecipeSamples.Chicken(ingredients: Many(30)));

        Assert.Contains("The children’s dinner is on page 3.", Texts(withOne[0], PrintedRole.Small));
        Assert.Contains($"The children’s dinner is on page {3 + flowed.Count - 1}.", flowed.SelectMany(p => Texts(p, PrintedRole.Small)));
    }

    [Fact]
    public void ATimedRecipeNumbersItsPagesAcrossTheIngredientsFlowToo()
    {
        var pages = RecipeDocument.Build(RecipeSamples.Timed(ingredients: Many(30)));
        var total = pages.Count + 1;

        Assert.Equal(Enumerable.Range(1, pages.Count).Select(n => $"{n} of {total}"), pages.Select(p => p.Printed.Single(i => i.Name == "page-number").Text));
        var ingredientPages = pages.TakeWhile(p => p.Title != "Steps").Count();
        Assert.Contains($"The children’s dinner is on page {ingredientPages + 2}.", pages.SelectMany(p => Texts(p, PrintedRole.Small)));
        Assert.Equal("Buttered noodles", pages[ingredientPages + 1].Title);
    }

    [Fact]
    public void AnEquipmentListThatCannotFitIsStillRefusedPlainly()
    {
        var extra = Enumerable.Range(1, 60).Select(i => $"Invented gadget number {i}").ToList();

        var ex = Assert.Throws<ArgumentException>(() => RecipeDocument.Build(RecipeSamples.Chicken(extra: extra)));
        Assert.Contains("equipment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FlowedPagesRenderAndKeepTheNotesMargin()
    {
        var pages = RecipeDocument.Build(RecipeSamples.Chicken(ingredients: Many(30)));
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));

        Assert.Equal(pages.Count, document.NumberOfPages);
        Assert.All(pages, p => Assert.Equal(RecipeIds.Notes, Assert.Single(p.Map.Targets).Id));
    }

    [Fact]
    public void FlowedIngredientLinesStillFitTheirColumn()
    {
        using var body = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);
        var ingredients = Enumerable.Range(1, 14).Select(i => new RecipeIngredient("Everything", $"{i} cup", $"MMMM WWWW invented wide ingredient number {i}, chopped")).ToList();
        var pages = RecipeDocument.Build(RecipeSamples.Chicken(ingredients: ingredients));

        Assert.True(pages.Count >= 2);
        foreach (var item in pages.SelectMany(p => p.Printed).Where(i => i.Name.StartsWith("ingredient-", StringComparison.Ordinal)))
        {
            Assert.True(body.MeasureText(item.Text!) <= item.Bounds.Width, item.Text);
        }
    }
}
