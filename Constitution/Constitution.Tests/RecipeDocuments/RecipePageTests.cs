using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;
using UglyToad.PdfPig.Content;

namespace Constitution.Tests.RecipeDocuments;

/// <summary>C-2.5.1: the first recipe page, held to book p.8 by reading the rendered PDF back.</summary>
public class RecipePageTests
{
    private static InkPageLayout Page(RecipeDocumentModel? model = null) => RecipeSamples.Page(model);

    private static IEnumerable<string> Texts(InkPageLayout page, PrintedRole role) =>
        page.Printed.Where(i => i.Role == role && i.Text is not null).Select(i => i.Text!);

    private static string Rendered(InkPageLayout page)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        return WorkoutCoverTests.TextOf(document.GetPage(1));
    }

    private static float Baseline(Page page, string word, int skip = 0) =>
        (float)(page.Height - page.GetWords().Where(w => w.Text == word).Skip(skip).First().Letters[0].StartBaseLine.Y);

    private static RecipeIngredient Line(string quantity, string name, string group = "Main") => new(group, quantity, name);

    [Fact]
    public void TheKickerTitleAndFooterAreAsDrawn()
    {
        var page = Page();
        var text = Rendered(page);

        Assert.Equal("Sheet-pan chicken", page.Title);
        Assert.Contains("RECIPE · FROM THE RECIPE BOX · FAMILY CARD", text);
        Assert.Contains("Constitution · Recipe · Tue Nov 17 · Lee", text);
        Assert.Contains("1 of 4", text);
        Assert.Equal("1 of 4", page.Printed.Single(i => i.Name == "page-number").Text);
    }

    [Fact]
    public void TheFooterSitsWhereTheWorkoutFooterDoes()
    {
        var recipe = Page();
        var workout = WorkoutDocument.Build(WorkoutSamples.Workout())[1];

        Assert.Equal(workout.Printed.Single(i => i.Name == "footer").Bounds, recipe.Printed.Single(i => i.Name == "footer").Bounds with { Width = 300f });
        Assert.Equal(workout.Printed.Single(i => i.Name == "page-number").Bounds, recipe.Printed.Single(i => i.Name == "page-number").Bounds);
    }

    [Fact]
    public void RendersOnePageAtTheTabletSize()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(RecipeDocument.Build(RecipeSamples.Chicken())));

        Assert.Equal(1, document.NumberOfPages);
        Assert.Equal((445d, 594d), (Math.Round(document.GetPage(1).Width), Math.Round(document.GetPage(1).Height)));
    }

    [Fact]
    public void TheHeadcountIsStatedFirst()
    {
        var page = Page();
        var first = page.Printed.Where(i => i.Role is PrintedRole.Strong or PrintedRole.Body or PrintedRole.Label && i.Name != "kicker" && i.OwnerId is null)
            .OrderBy(i => i.Bounds.Top).First();

        Assert.Equal(PrintedRole.Strong, first.Role);
        Assert.Equal("With potatoes · for 4: 2 adults, 2 children", first.Text);
        Assert.Contains("With potatoes · for 4: 2 adults, 2 children", Rendered(page));
    }

    [Theory]
    [InlineData(2, 2, 1, "for 5: 2 adults, 2 children, 1 guest")]
    [InlineData(2, 2, 3, "for 7: 2 adults, 2 children, 3 guests")]
    [InlineData(1, 1, 0, "for 2: 1 adult, 1 child")]
    [InlineData(2, 0, 0, "for 2: 2 adults")]
    public void GuestsAreCountedAndPluralsAgree(int adults, int children, int guests, string expected)
    {
        var page = Page(RecipeSamples.Chicken(headcount: new RecipeHeadcount(adults, children, guests)));

        Assert.Contains(expected, string.Join(" · ", Texts(page, PrintedRole.Strong)));
    }

    [Fact]
    public void WithoutAWithLineTheSentenceStartsWithFor()
    {
        var page = Page(RecipeSamples.Chicken(withLine: null));

        Assert.Equal("For 4: 2 adults, 2 children", Texts(page, PrintedRole.Strong).First());
    }

    [Fact]
    public void TheTimingLineNamesDinnerStartAndHandsOnTime()
    {
        var text = string.Join(" · ", Texts(Page(), PrintedRole.Small));

        Assert.Contains("Dinner 6:15 pm · start 5:15 pm · 60 minutes, 20 of them hands-on", text);
        Assert.DoesNotContain("hands-on", string.Join(" ", Texts(Page(RecipeSamples.Chicken(handsOn: null)), PrintedRole.Small)));
    }

    [Fact]
    public void GroupsRunInTheOrderTheyAreUsed()
    {
        var labels = Texts(Page(), PrintedRole.Label).Where(t => t is not "NOTES" and not "HAVE OUT" && !t.StartsWith("RECIPE", StringComparison.Ordinal));

        Assert.Equal(["CHICKEN", "POTATOES", "TO FINISH"], labels);
    }

    [Fact]
    public void QuantitiesAreScaledFromTheCardToTheHeadcount()
    {
        Assert.Equal(["2 lb", "1 tbsp", "1 tsp", "1½ tsp", "2 lb", "3 tbsp", "½ tsp", "1", "2 tbsp"], Texts(Page(), PrintedRole.Mono));
    }

    [Fact]
    public void ABiggerTableScalesTheSameCardUp()
    {
        var page = Page(RecipeSamples.Chicken(headcount: new RecipeHeadcount(3, 2, 1)));

        Assert.Equal("3 lb", Texts(page, PrintedRole.Mono).First());
        Assert.Contains("Quantities as the card has them: 6 servings.", Texts(page, PrintedRole.Small));
    }

    [Fact]
    public void QuantitiesEndAtOneEdgeAndNamesStartAtAnother()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Page()]));
        var letters = document.GetPage(1).Letters.Where(l => l.GlyphRectangle.Top < 560 && l.GlyphRectangle.Bottom > 140).ToList();
        var mono = letters.Where(l => l.FontName.Contains("Cascadia", StringComparison.OrdinalIgnoreCase) && l.Location.X < 100).ToList();
        var rows = mono.GroupBy(l => Math.Round(l.StartBaseLine.Y)).ToList();

        Assert.Equal(9, rows.Count);
        Assert.All(rows, row => Assert.Equal(96.0, row.Max(l => l.EndBaseLine.X), 0.6));

        var rowBaselines = rows.Select(r => r.Key).ToList();
        var names = letters.Where(l => l.Location.X > 100 && rowBaselines.Contains(Math.Round(l.StartBaseLine.Y))).GroupBy(l => Math.Round(l.StartBaseLine.Y)).ToList();
        Assert.Equal(9, names.Count);
        Assert.All(names, row => Assert.Equal(108.0, row.Min(l => l.Location.X), 1.5));
    }

    [Fact]
    public void TheMonoInkBoxIsTheFacesOwnAndSitsOnTheNamesLine()
    {
        // The painter sets a baseline from the face's own descent, so the layout's table must agree or mono sits off the line beside it.
        using var mono = new SKFont(EmbeddedTypefaces.Mono, PaperTypeScale.Mono);
        var item = Page().Printed.First(i => i.Role == PrintedRole.Mono);
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Page()]));
        var letters = document.GetPage(1).Letters;
        var quantity = letters.First(l => l.FontName.Contains("Cascadia", StringComparison.OrdinalIgnoreCase));
        var name = letters.First(l => l.Value == "b" && l.Location.X > 100);

        Assert.Equal(mono.Metrics.Descent - mono.Metrics.Ascent, item.Bounds.Height, 0.1f);
        Assert.Equal(name.StartBaseLine.Y, quantity.StartBaseLine.Y, 0.05);
    }

    [Fact]
    public void QuantitiesPrintInMonoAtTheMonoRung()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Page()]));
        var quantity = document.GetPage(1).Letters.First(l => l.Value == "½");

        Assert.Equal(PaperTypeScale.Mono, (float)quantity.PointSize, 0.1f);
        Assert.Contains("Cascadia", quantity.FontName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheBookSampleSitsOnTheBooksBaselines()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Page()]));
        var page = document.GetPage(1);

        Assert.Equal(112f, Baseline(page, "With"), 1.5f);
        Assert.Equal(162f, Baseline(page, "CHICKEN"), 1.5f);
        Assert.Equal(180f, Baseline(page, "bone-in"), 1.5f);
        Assert.Equal(237f, Baseline(page, "salt,"), 1.5f);
        Assert.Equal(260f, Baseline(page, "POTATOES"), 1.5f);
        Assert.Equal(339f, Baseline(page, "FINISH"), 1.5f);
        Assert.Equal(419f, Baseline(page, "HAVE"), 1.5f);
        Assert.Equal(437f, Baseline(page, "Large"), 1.5f);
        Assert.Equal(519f, Baseline(page, "Scaled"), 1.5f);
        Assert.Equal(533f, Baseline(page, "children’s"), 1.5f);
    }

    [Fact]
    public void HaveOutIsDerivedFromTheSteps()
    {
        var items = Texts(Page(), PrintedRole.Body).Where(t => t.StartsWith('·'));

        Assert.Equal(["·  Large rimmed sheet pan", "·  Chef’s knife and board", "·  Large bowl", "·  Instant-read thermometer"], items);
    }

    [Fact]
    public void SaysWhatTheCardWasScaledFrom()
    {
        Assert.Contains("Scaled from the card’s 6 servings.", Texts(Page(), PrintedRole.Small));
    }

    [Fact]
    public void ACardMadeForTheSameTableIsPrintedAsWritten()
    {
        var page = Page(RecipeSamples.Chicken(cardServings: 4));

        Assert.Contains("Quantities as the card has them: 4 servings.", Texts(page, PrintedRole.Small));
        Assert.Equal("3 lb", Texts(page, PrintedRole.Mono).First());
    }

    [Fact]
    public void AQuantityThatCannotBeReadIsKeptAndNamed()
    {
        var page = Page(RecipeSamples.Chicken(ingredients:
        [
            Line("2 lb", "chicken thighs"), Line("a pinch", "flaky salt, to finish"), Line("1-2 tsp", "chili flakes"),
        ]));

        var small = string.Join(" ", Texts(page, PrintedRole.Small));
        Assert.Contains("Not scaled, as the card has them: flaky salt, chili flakes.", small);
        Assert.Equal(["1 1/3 lb", "a pinch", "1-2 tsp"], Texts(page, PrintedRole.Mono));
    }

    [Fact]
    public void TheChildrensDinnerIsNamedOnlyWhenThereIsOne()
    {
        Assert.Contains("The children’s dinner is on page 3.", Texts(Page(), PrintedRole.Small));
        Assert.DoesNotContain(Texts(Page(RecipeSamples.Chicken(kidsPage: null)), PrintedRole.Small), t => t.Contains("children’s", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOnlyTargetIsTheNotesMargin()
    {
        var page = Page();
        var notes = Assert.Single(page.Map.Targets);

        Assert.Equal(RecipeIds.Notes, notes.Id);
        Assert.Equal(InkTargetKind.NotesMargin, notes.Kind);
        Assert.Equal(318f, notes.Bounds.Left);
        Assert.Contains("NOTES", Rendered(page));
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void TheNotesMarginIsTheSameMarginAsTheWorkoutPages()
    {
        Assert.Equal("notes", RecipeIds.Notes);
        Assert.Equal(WorkoutIds.Notes, RecipeIds.Notes);
        var workout = WorkoutDocument.Build(WorkoutSamples.Workout())[1].Map.ById(WorkoutIds.Notes);

        Assert.Equal(workout.Bounds, Page().Map.ById(RecipeIds.Notes).Bounds);
    }

    [Fact]
    public void NoWordCrossesIntoTheNotesMargin()
    {
        foreach (var model in LongModels())
        {
            using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Page(model)]));
            var letters = document.GetPage(1).Letters.Where(l => l.GlyphRectangle.Top < 560 && l.GlyphRectangle.Top > 90).ToList();

            Assert.DoesNotContain(letters, l => l.GlyphRectangle.Left < 314 && l.GlyphRectangle.Right > 308);
        }
    }

    [Fact]
    public void LongIngredientLinesWrapWithinTheirColumnByRealGlyphWidths()
    {
        using var body = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);
        var measured = 0;

        foreach (var model in LongModels())
        {
            var page = Page(model);
            foreach (var item in page.Printed.Where(i => i.Name.StartsWith("ingredient-", StringComparison.Ordinal)))
            {
                Assert.True(body.MeasureText(item.Text!) <= item.Bounds.Width, $"'{item.Text}' is {body.MeasureText(item.Text!):0} points in a {item.Bounds.Width:0} point column.");
                measured++;
            }
        }

        Assert.True(measured >= 20, $"Only {measured} lines were measured.");
    }

    [Fact]
    public void LongHeadcountEquipmentAndNoteLinesFitTheirColumnToo()
    {
        using var strong = new SKFont(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Body);
        using var body = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);
        using var small = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary);
        var lines = 0;

        foreach (var model in LongModels())
        {
            foreach (var item in Page(model).Printed.Where(i => i.Text is not null && i.Role is PrintedRole.Strong or PrintedRole.Small or PrintedRole.Body && i.OwnerId is null))
            {
                var font = item.Role == PrintedRole.Strong ? strong : item.Role == PrintedRole.Small ? small : body;
                var width = font.MeasureText(item.Text!);
                var room = item.Name.StartsWith("ingredient-", StringComparison.Ordinal) ? item.Bounds.Width : 270f;

                Assert.True(width <= room, $"'{item.Text}' is {width:0} points in a {room:0} point column.");
                lines++;
            }
        }

        Assert.True(lines >= 40);
    }

    [Fact]
    public void QuantitiesInTheMonoColumnFitIt()
    {
        using var mono = new SKFont(EmbeddedTypefaces.Mono, PaperTypeScale.Mono);
        var quantities = new[] { "1 2/3 cups", "1 1/3 tbsp", "12 oz", "1½ cups", "2 1/3 lb", "0.14 cup", "10 cloves" };

        foreach (var quantity in quantities.Where(q => q.Length <= 9))
        {
            Assert.True(mono.MeasureText(quantity) <= 96f - 36f, quantity);
        }
    }

    [Fact]
    public void ALongQuantityLeadsTheNameInsteadOfOverflowingTheColumn()
    {
        var page = Page(RecipeSamples.Chicken(ingredients: [Line("1 cup (240 ml)", "coconut milk")]));

        Assert.Empty(Texts(page, PrintedRole.Mono));
        Assert.Contains(page.Printed, i => i.Text == "1 cup (240 ml) coconut milk");
    }

    [Fact]
    public void ManyIngredientsFlowOnRatherThanBeingSquashed()
    {
        var many = Enumerable.Range(1, 20).Select(i => Line("1 tsp", $"spice number {i}")).ToList();

        Assert.True(RecipeDocument.Build(RecipeSamples.Chicken(ingredients: many)).Count >= 2);
    }

    [Fact]
    public void EveryLongPageIsFitToPrint()
    {
        foreach (var model in LongModels())
        {
            Assert.Empty(Page(model).Violations());
        }
    }

    [Fact]
    public void OnlyTheHeaderRuleIsColouredAndEverythingElseIsMuted()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Page()]));
        var page = document.GetPage(1);
        var neutral = new[]
        {
            PaperColorTokens.Paper, PaperColorTokens.PaperBright, PaperColorTokens.PaperDeep, PaperColorTokens.PaperShade,
            PaperColorTokens.Hairline, PaperColorTokens.Ink, PaperColorTokens.InkSoft, PaperColorTokens.InkMuted,
        }.Select(PdfReadBack.Rgb).ToList();
        var header = PdfReadBack.Rgb(AppIdentityTokens.ConstitutionDocument);

        var coloured = PdfReadBack.PaintedColours(page)
            .Where(c => !neutral.Any(n => PdfReadBack.SameColour((c.R, c.G, c.B), n)) && !(c.R > 0.99 && c.G > 0.99 && c.B > 0.99))
            .ToList();

        Assert.Single(coloured);
        Assert.Equal("stroke", coloured[0].Use);
        Assert.True(PdfReadBack.SameColour((coloured[0].R, coloured[0].G, coloured[0].B), header));

        var muted = PdfReadBack.Rgb(PaperColorTokens.InkMuted);
        var rule = Page().Printed.Single(i => i.Name == "equipment-rule");
        var stroked = PdfReadBack.Paths(page).Where(p => p.Stroked && PdfReadBack.Same(p.Bounds, rule.Bounds)).ToList();
        Assert.NotEmpty(stroked);
        Assert.All(stroked, p => Assert.True(PdfReadBack.SameColour(p.Stroke, muted)));
    }

    [Fact]
    public void OnlyRegisteredTypeRungsArePrinted()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Page()]));
        var allowed = new[] { PaperTypeScale.Title, PaperTypeScale.Body, PaperTypeScale.Secondary, PaperTypeScale.Label, PaperTypeScale.Mono };
        var sizes = document.GetPage(1).Letters.Select(l => (float)l.PointSize).Distinct().ToList();

        Assert.All(sizes, size => Assert.Contains(allowed, rung => Math.Abs(rung - size) < 0.1f));
        Assert.True(sizes.Count >= 4);
    }

    [Fact]
    public void TheModelIsChecked()
    {
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken() with { Title = " " }));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken() with { Cook = "" }));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken() with { CardServings = 0 }));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken(headcount: new RecipeHeadcount(0, 0))));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken(ingredients: [])));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken(ingredients: [Line("1", " ")])));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken(handsOn: 61)));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken() with { StartAt = RecipeSamples.Tuesday.AddHours(20) }));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken(kidsPage: 9)));
        Assert.Throws<ArgumentException>(() => Page(RecipeSamples.Chicken() with { Pages = 0, KidsDinnerPage = null }));
    }

    private static IEnumerable<RecipeDocumentModel> LongModels()
    {
        string[][] sets =
        [
            [
                "boneless skinless chicken thighs, trimmed of any visible fat and cut into bite-sized pieces",
                "fresh flat-leaf parsley leaves and tender stems, finely chopped",
                "unsalted butter, softened to room temperature",
            ],
            [
                "MMMM WWWW mixed capitals in an unusually wide invented ingredient line",
                "extra-virgin olive oil, plus a little more for drizzling at the end",
                "dried oregano, rubbed between the fingers first",
            ],
            [
                "canned fire-roasted diced tomatoes with their juices, drained very well",
                "Worcestershire sauce, Wondra flour, Wagyu beef and Wasabi mix",
                "toasted pumpkin seeds, lightly salted, or roasted sunflower seeds",
            ],
        ];

        foreach (var set in sets)
        {
            var ingredients = set.Select((name, i) => new RecipeIngredient(i < 2 ? "First" : "Then", $"{i + 1} cup", name)).ToList();
            yield return RecipeSamples.Chicken(ingredients: ingredients);
            yield return RecipeSamples.Chicken(ingredients: ingredients, headcount: new RecipeHeadcount(2, 2, 3), withLine: "With roasted potatoes and a green salad");
        }
    }
}
