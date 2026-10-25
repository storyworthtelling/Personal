using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;
using UglyToad.PdfPig.Content;

namespace Constitution.Tests.RecipeDocuments;

/// <summary>C-2.5.3: the children's dinner on its own page, on nights they eat differently (book p.9).</summary>
public class KidsPageTests
{
    private static readonly DateTime Dinner = RecipeSamples.Tuesday.AddHours(18).AddMinutes(15);

    private static IReadOnlyList<InkPageLayout> Pages(RecipeDocumentModel? model = null) => RecipeDocument.Build(model ?? RecipeSamples.Timed());

    private static InkPageLayout Kids(RecipeDocumentModel? model = null) => Pages(model)[2];

    private static IEnumerable<string> Texts(InkPageLayout page, PrintedRole role) =>
        page.Printed.Where(i => i.Role == role && i.Text is not null).Select(i => i.Text!);

    private static float Baseline(Page page, string word, int skip = 0) =>
        (float)(page.Height - page.GetWords().Where(w => w.Text == word).Skip(skip).First().Letters[0].StartBaseLine.Y);

    [Fact]
    public void OnlyOnNightsTheyEatDifferently()
    {
        var different = Pages();
        var same = Pages(RecipeSamples.Timed(steps: RecipeSamples.TimedSteps().Select(s => s with { KidsHandOff = false }).ToList(), noKids: true));

        Assert.Contains(different, p => p.Title == "Buttered noodles");
        Assert.DoesNotContain(same, p => p.Title == "Buttered noodles");
        Assert.Equal(3, different.Count);
        Assert.Equal(2, same.Count);
        Assert.DoesNotContain(same.SelectMany(p => p.Printed), i => i.Text is not null && i.Text.Contains("RECIPE · THE CHILDREN", StringComparison.Ordinal));
    }

    [Fact]
    public void CountsNothing()
    {
        var text = string.Join(" ", Kids().Printed.Select(i => i.Text ?? string.Empty));
        var words = new Regex(@"calori|kcal|protein|carb|\bfat\b|sugar|nutri|macro|\bgrams?\b|points", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        Assert.DoesNotMatch(words, text);
        var members = typeof(RecipeKidsMeal).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Concat(typeof(RecipeIngredient).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name));
        Assert.DoesNotContain(members, m => words.IsMatch(m));
    }

    [Fact]
    public void TheKidsPageIsAsDrawn()
    {
        var page = Kids();

        Assert.Equal("Buttered noodles", page.Title);
        Assert.Contains("RECIPE · THE CHILDREN’S DINNER TONIGHT", Texts(page, PrintedRole.Label));
        Assert.Equal("Constitution · Recipe · Tue Nov 17 · Lee", page.Printed.Single(i => i.Name == "footer").Text);
        Assert.Equal("3 of 4", page.Printed.Single(i => i.Name == "page-number").Text);
        Assert.Equal("For Robin and Juniper", Texts(page, PrintedRole.Strong).Single());
        Assert.Contains("INGREDIENTS", Texts(page, PrintedRole.Label));
    }

    [Fact]
    public void OneLineSaysWhyTonightIsDifferent()
    {
        var small = string.Join(" ", Texts(Kids(), PrintedRole.Small).Where(t => !t.StartsWith("Constitution", StringComparison.Ordinal)));

        Assert.Contains("Same table, same time. The chicken has paprika they don’t like.", small);
    }

    [Theory]
    [InlineData("Robin", "For Robin")]
    [InlineData("Robin|Juniper", "For Robin and Juniper")]
    [InlineData("Robin|Juniper|Lee", "For Robin, Juniper and Lee")]
    public void TheChildrenAreNamedInPlainWords(string names, string expected)
    {
        var kids = RecipeSamples.Kids() with { Children = names.Split('|') };

        Assert.Equal(expected, Texts(Kids(RecipeSamples.Timed(kids: kids)), PrintedRole.Strong).Single());
    }

    [Fact]
    public void TheIngredientsKeepTheirQuantitiesInMonoAsTheCardHasThem()
    {
        var page = Kids();

        Assert.Equal(["6 oz", "1 tbsp", "½ cup", "2 tbsp", "1"], Texts(page, PrintedRole.Mono));
        Assert.Equal(["small pasta shapes", "butter", "frozen peas", "parmesan", "carrot, in sticks"],
            page.Printed.Where(i => i.Name.StartsWith("ingredient-", StringComparison.Ordinal)).Select(i => i.Text!));
    }

    [Fact]
    public void TheStepsAreTimedToTheSameDinner()
    {
        var page = Kids();

        Assert.Equal(["5:15", "5:55", "6:07", "6:15"], Texts(page, PrintedRole.Time));
        Assert.Contains("Dinner, with carrot sticks.", Texts(page, PrintedRole.Body));
        Assert.Contains("Pasta in. Peas in", string.Join(" ", Texts(page, PrintedRole.Body)));

        var steps = RecipeSamples.Timed();
        var chicken = RecipeSchedule.Plan(steps.Steps!, steps.DinnerAt, steps.StartAt);
        var kids = RecipeSchedule.Plan(steps.KidsMeal!.Steps, steps.DinnerAt, steps.StartAt);
        Assert.Equal(chicken.Rows[^1].At, kids.Rows[^1].At);
    }

    [Fact]
    public void TheKidsStepsNeverStartBeforeNowEither()
    {
        var page = Kids(RecipeSamples.Timed(notBefore: Dinner.AddMinutes(-30)));

        Assert.Equal(["5:45", "5:55", "6:07", "6:15"], Texts(page, PrintedRole.Time));
    }

    [Fact]
    public void TheRowsSitWhereTheBookPutsThem()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Kids()]));
        var page = document.GetPage(1);

        Assert.Equal(112f, Baseline(page, "For"), 1.5f);
        Assert.Equal(162f, Baseline(page, "INGREDIENTS"), 1.5f);
        Assert.Equal(180f, Baseline(page, "pasta"), 1.5f);
        Assert.Equal(256f, Baseline(page, "carrot,"), 1.5f);
        Assert.Equal(299f, Baseline(page, "5:15"), 1.5f);

        // The book steps these rows a flat 48 points apart; the rule that serves the steps page (46 for a one-line step, 50 for two) puts them 2 points higher.
        Assert.Equal(345f, Baseline(page, "5:55"), 1f);
        Assert.Equal(441f, Baseline(page, "6:15"), 1f);
    }

    [Fact]
    public void TheNotesMarginIsTheSameMarginAsEveryRecipePage()
    {
        var page = Kids();
        var notes = Assert.Single(page.Map.Targets);

        Assert.Equal(RecipeIds.Notes, notes.Id);
        Assert.Equal(Pages()[0].Map.ById(RecipeIds.Notes).Bounds, notes.Bounds);
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void LongKidsLinesWrapWithinTheirColumnByRealGlyphWidths()
    {
        using var body = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);
        using var small = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary);
        using var strong = new SKFont(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Body);
        var kids = RecipeSamples.Kids() with
        {
            Children = ["Maximilian-Wilhelmina", "Wolfgang", "Juniper", "Robin", "Marigold"],
            WhyLine = "Same table, same time, but the chicken has smoked paprika and garlic that they will not touch at all, which is a long way to say it.",
            Ingredients =
            [
                new("Main", "6 oz", "small pasta shapes, any kind at all that they like the look of tonight"),
                new("Main", "1 cup (240 ml)", "whole milk, warmed first"),
                new("Main", "2 tbsp", "WWWW MMMM mixed capitals in an unusually wide invented ingredient"),
            ],
            Steps = [new("Wondra flour, Worcestershire sauce, Wagyu beef and Wasabi mix, folded through quickly", 20), new("Rest.", 20, Aside: "Why: it needs the time, and the pasta needs the heat to be even all through.")],
            DinnerLine = "Dinner, with carrot sticks and a little extra something besides.",
        };
        var page = Kids(RecipeSamples.Timed(kids: kids));
        var measured = 0;

        foreach (var item in page.Printed.Where(i => i.Text is not null && i.OwnerId is null && i.Role is PrintedRole.Body or PrintedRole.Small or PrintedRole.Strong))
        {
            var font = item.Role == PrintedRole.Strong ? strong : item.Role == PrintedRole.Small ? small : body;
            var room = item.Name is "footer" or "page-number" ? 300f : item.Bounds.Width;
            Assert.True(font.MeasureText(item.Text!) <= room, $"'{item.Text}' is {font.MeasureText(item.Text!):0} points in a {room:0} point column.");
            measured++;
        }

        Assert.True(measured >= 12, $"Only {measured} lines were measured.");
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void OnlyTheHeaderRuleIsColouredAndEverythingElseIsMuted()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Kids()]));
        var neutral = new[]
        {
            PaperColorTokens.Paper, PaperColorTokens.PaperBright, PaperColorTokens.PaperDeep, PaperColorTokens.PaperShade,
            PaperColorTokens.Hairline, PaperColorTokens.Ink, PaperColorTokens.InkSoft, PaperColorTokens.InkMuted,
        }.Select(PdfReadBack.Rgb).ToList();

        var coloured = PdfReadBack.PaintedColours(document.GetPage(1))
            .Where(c => !neutral.Any(n => PdfReadBack.SameColour((c.R, c.G, c.B), n)) && !(c.R > 0.99 && c.G > 0.99 && c.B > 0.99))
            .ToList();

        Assert.Single(coloured);
        Assert.True(PdfReadBack.SameColour((coloured[0].R, coloured[0].G, coloured[0].B), PdfReadBack.Rgb(AppIdentityTokens.ConstitutionDocument)));
    }

    [Fact]
    public void OnlyRegisteredTypeRungsArePrinted()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Kids()]));
        var allowed = new[] { PaperTypeScale.Title, PaperTypeScale.Body, PaperTypeScale.Secondary, PaperTypeScale.Label, PaperTypeScale.Mono };

        Assert.All(document.GetPage(1).Letters.Select(l => (float)l.PointSize).Distinct(), size => Assert.Contains(allowed, rung => Math.Abs(rung - size) < 0.1f));
    }

    [Fact]
    public void TheKidsPageIsTheLastRecipePageBeforeTheRating()
    {
        var pages = Pages();

        Assert.Equal("Buttered noodles", pages[^1].Title);
        Assert.Equal("3 of 4", pages[^1].Printed.Single(i => i.Name == "page-number").Text);
    }

    [Fact]
    public void ALongKidsDinnerFlowsToAContinuationPage()
    {
        var steps = Enumerable.Range(1, 16).Select(n => new RecipeClockStep($"Kids step number {n}: stir gently and keep an eye on the pot until it is ready.", 3, Aside: n % 2 == 0 ? "Not too hot." : null)).ToList();
        var pages = Pages(RecipeSamples.Timed(kids: RecipeSamples.Kids() with { Steps = steps }));
        var kids = pages.Skip(2).ToList();

        Assert.True(kids.Count >= 2);
        Assert.All(kids, p => Assert.Equal("Buttered noodles", p.Title));
        Assert.Contains("RECIPE · THE CHILDREN’S DINNER TONIGHT · CONTINUED", Texts(kids[1], PrintedRole.Label));
        Assert.Equal(17, kids.SelectMany(p => Texts(p, PrintedRole.Time)).Count());
        Assert.All(pages, p => Assert.Empty(p.Violations()));
        Assert.Equal($"{pages.Count} of {pages.Count + 1}", pages[^1].Printed.Single(i => i.Name == "page-number").Text);
    }

    [Fact]
    public void TheKidsMealIsChecked()
    {
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(kids: RecipeSamples.Kids() with { Title = " " })));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(kids: RecipeSamples.Kids() with { Children = [] })));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(kids: RecipeSamples.Kids() with { WhyLine = "" })));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(kids: RecipeSamples.Kids() with { Ingredients = [] })));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(kids: RecipeSamples.Kids() with { Steps = [] })));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(kids: RecipeSamples.Kids() with { Steps = [new("Wait.", 5, Morning: true)] })));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(kids: RecipeSamples.Kids() with { ShortName = "" })));
    }

    [Fact]
    public void AKidsMealWithoutTimedStepsOnTheMainCardIsRefused()
    {
        var legacy = RecipeSamples.Chicken() with { KidsMeal = RecipeSamples.Kids() };

        Assert.Throws<ArgumentException>(() => RecipeDocument.Build(legacy));
    }
}
