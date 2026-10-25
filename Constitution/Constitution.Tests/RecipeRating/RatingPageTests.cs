using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using Constitution.Tests.RecipeDocuments;
using SkiaSharp;
using Storyworthtelling.Paper;
using static Constitution.Tests.RecipeRating.RatingSamples;

namespace Constitution.Tests.RecipeRating;

/// <summary>C-2.5.4: the rating page, held to book p.9 (the last page of tonight's recipe).</summary>
public class RatingPageTests
{
    private static InkTarget Lane(InkPageLayout page, int row, int lane) => page.Map.ById($"rate-{row}-{lane}");

    [Fact]
    public void PreUnderlinesTheLastRating()
    {
        var page = Page();

        // Sam Loved, Lee Fine, Robin Loved: each underline belongs to that person's lane, nobody else's.
        Assert.Equal(["rate-1-1", "rate-2-2", "rate-3-1"], Items(page, "rate-").Where(i => i.Name.EndsWith("-underline", StringComparison.Ordinal)).Select(i => i.OwnerId!));
        foreach (var (row, lane) in new[] { (1, 1), (2, 2), (3, 1) })
        {
            var underline = Item(page, $"rate-{row}-underline");
            var target = Lane(page, row, lane);
            Assert.Equal(PrintedRole.Rule, underline.Role);
            Assert.Equal(0f, underline.Bounds.Height);
            Assert.Equal(target.Bounds.CentreX, underline.Bounds.CentreX, 0.01);
            Assert.True(underline.Bounds.Width < target.Bounds.Width);
            Assert.InRange(underline.Bounds.Top, target.Bounds.CentreY + 5f, target.Bounds.Bottom);
        }
    }

    [Fact]
    public void EveryLastRatingLandsOnItsOwnLane()
    {
        foreach (var (verdict, lane) in new[] { (TasteVerdict.Loved, 1), (TasteVerdict.Fine, 2), (TasteVerdict.NeverAgain, 3) })
        {
            var page = Page(Of(Sam(verdict)));

            Assert.Equal($"rate-1-{lane}", Item(page, "rate-1-underline").OwnerId);
        }
    }

    [Fact]
    public void AFirstTimeRowSaysSoInsteadOfAnUnderline()
    {
        var page = Page();

        Assert.Equal("FIRST TIME · PLEASE CIRCLE", Item(page, "rate-4-first-time").Text);
        Assert.Equal(PrintedRole.Label, Item(page, "rate-4-first-time").Role);
        Assert.DoesNotContain(page.Printed, i => i.Name == "rate-4-underline");
        Assert.Single(page.Printed, i => i.Text == "FIRST TIME · PLEASE CIRCLE");
        Assert.DoesNotContain(Page(AllKnown()).Printed, i => i.Text == "FIRST TIME · PLEASE CIRCLE");
    }

    [Fact]
    public void EachRowNamesTheDishThatPersonAte()
    {
        var page = Page();

        Assert.Equal(["Sam", "Lee", "Robin", "Juniper"], Enumerable.Range(1, 4).Select(n => Item(page, $"rate-{n}-name").Text));
        Assert.Equal(
            ["Sheet-pan chicken", "Sheet-pan chicken", "Buttered noodles", "Buttered noodles"],
            Enumerable.Range(1, 4).Select(n => Item(page, $"rate-{n}-dish-1").Text));

        // The dish sits under its own person's name, not a neighbour's.
        for (var n = 1; n <= 4; n++)
        {
            Assert.True(Item(page, $"rate-{n}-dish-1").Bounds.Top > Item(page, $"rate-{n}-name").Bounds.Top);
            Assert.True(Item(page, $"rate-{n}-dish-1").Bounds.Bottom < Lane(page, n, 1).Bounds.Bottom + 30f);
        }
    }

    [Fact]
    public void ALongDishWrapsToASecondLineAndTheRowGrowsToHoldIt()
    {
        var page = Page(Of(Sam() with { Dish = "Roasted tomato and white bean soup with crusty bread" }, Lee()));

        Assert.NotNull(page.Printed.SingleOrDefault(i => i.Name == "rate-1-dish-2"));
        Assert.Empty(page.Violations());
        Assert.EndsWith("…", Item(page, "rate-1-dish-2").Text!, StringComparison.Ordinal);
    }

    [Fact]
    public void EachRowHasThreeLanesWithThePrintedWords()
    {
        var page = Page();

        for (var row = 1; row <= 4; row++)
        {
            var lanes = Enumerable.Range(1, 3).Select(n => Lane(page, row, n)).ToList();
            Assert.Equal(["Loved", "Fine", "Never again"], lanes.Select(l => l.Meaning));
            Assert.All(lanes, l => Assert.Equal(InkTargetKind.CircleLane, l.Kind));
            Assert.All(lanes, l => Assert.Equal($"rate-{row}", l.Group));
            Assert.Equal([TasteVerdict.Loved, TasteVerdict.Fine, TasteVerdict.NeverAgain], lanes.Select(l => TasteWords.FromPrinted(l.Meaning)!.Value));
            Assert.Equal(["Loved", "Fine", "Never again"], lanes.Select(l => Item(page, $"{l.Id}-word").Text));
        }
    }

    [Fact]
    public void TheNeverAgainRuleIsPrintedAtTheFoot()
    {
        var page = Page(AllKnown());
        var lines = new[] { Item(page, "rule-line-1"), Item(page, "rule-line-2") };

        Assert.Equal("“Never again” from one person means this dish is planned only", lines[0].Text);
        Assert.Equal("when they eat something else or are away.", lines[1].Text);
        Assert.All(lines, l => Assert.Equal(PrintedRole.Small, l.Role));
        Assert.All(lines, l => Assert.True(l.Bounds.Top > Lane(page, 4, 1).Bounds.Bottom));
        Assert.All(lines, l => Assert.True(l.Bounds.Bottom < page.Map.ById(RecipeIds.DidntCook).Bounds.Top));
    }

    [Fact]
    public void TheDidntCookBoxIsAnOwnTickWithItsWords()
    {
        var page = Page();
        var box = page.Map.ById(RecipeIds.DidntCook);

        Assert.Equal("didnt-cook", RecipeIds.DidntCook);
        Assert.Equal(InkTargetKind.StandardTick, box.Kind);
        Assert.Equal((InkTargets.StandardTickSize, InkTargets.StandardTickSize), (box.Bounds.Width, box.Bounds.Height));
        Assert.Equal(PageLeft, box.Bounds.Left);
        Assert.Equal("We didn’t cook this tonight", Item(page, "didnt-cook-label").Text);
        Assert.Equal(PrintedRole.Strong, Item(page, "didnt-cook-label").Role);
        Assert.Equal("Tick it and the ratings are ignored; its fresh ingredients are written off.", Item(page, "didnt-cook-hint").Text);
        Assert.Equal(RecipeIds.DidntCook, Item(page, "didnt-cook-label").OwnerId);
    }

    private const float PageLeft = 36f;

    [Fact]
    public void AllFourRowsAndTheFootAreWhereTheBookDrawsThem()
    {
        var page = Page(AllKnown());

        for (var n = 1; n <= 4; n++)
        {
            var top = 150f + 66f * (n - 1);
            Assert.Equal((127f, top + 12f, 88f, 32f), Dims(Lane(page, n, 1)));
            Assert.Equal((215f, top + 12f, 88f, 32f), Dims(Lane(page, n, 2)));
            Assert.Equal((303f, top + 12f, 104f, 32f), Dims(Lane(page, n, 3)));
            Assert.Equal(top, Item(page, $"rule-{n}").Bounds.Top);
            Assert.Equal(top + 26f, Baseline(page, $"rate-{n}-name"));
            Assert.Equal(top + 41f, Baseline(page, $"rate-{n}-dish-1"));
            Assert.Equal(top + 31f, Baseline(page, $"rate-{n}-1-word"));
            Assert.Equal(215f, Item(page, $"rate-{n}-divider-1").Bounds.Left);
            Assert.Equal(303f, Item(page, $"rate-{n}-divider-2").Bounds.Left);
        }

        Assert.Equal(414f, Item(page, "rule-5").Bounds.Top);
        Assert.Equal(112f, Baseline(page, "intro-1"));
        Assert.Equal(126f, Baseline(page, "intro-2"));
        Assert.Equal(444f, Baseline(page, "rule-line-1"));
        Assert.Equal(458f, Baseline(page, "rule-line-2"));
        Assert.Equal(480f, Item(page, "foot-rule").Bounds.Top);
        Assert.Equal(new InkRect(36f, 496f, 18f, 18f), page.Map.ById(RecipeIds.DidntCook).Bounds);
        Assert.Equal(509f, Baseline(page, "didnt-cook-label"));
        Assert.Equal(523f, Baseline(page, "didnt-cook-hint"));
    }

    [Fact]
    public void AFirstTimeLabelClearsItsLanesSoItSitsLowerThanTheBookDrawsIt()
    {
        var page = Page();

        // The book's label is 0.9 points under the lane; the page's own rule keeps 8, so the label is 7.5 points lower.
        Assert.Equal(348f + 62.5f, Baseline(page, "rate-4-first-time"));
        Assert.Empty(page.Violations());
        Assert.Equal(414f, Item(page, "rule-5").Bounds.Top);
    }

    [Fact]
    public void TheHeaderAndIntroAreAsDrawn()
    {
        var page = Page();

        Assert.Equal("Who liked it?", page.Title);
        Assert.Equal("RECIPE · TONIGHT’S RATINGS · THE COOK RATES FOR EVERYONE", Item(page, "kicker").Text);
        Assert.Equal("Underlined is each person’s last rating of this dish.", Item(page, "intro-1").Text);
        Assert.Equal("Circle only if it’s new or has changed. No circle means unchanged.", Item(page, "intro-2").Text);
        Assert.Equal("Constitution · Recipe · Tue Nov 17 · Lee", Item(page, "footer").Text);
        Assert.Equal("4 of 4", Item(page, "page-number").Text);
    }

    [Fact]
    public void TheBookDrawsNoNotesMarginOnThisPage()
    {
        // The lanes run to 407; a 91 point margin cannot sit beside them. Growing one is a decision about the page, not a drift.
        Assert.DoesNotContain(Page().Map.Targets, t => t.Kind == InkTargetKind.NotesMargin);
        Assert.DoesNotContain(Page().Printed, i => i.Text == "NOTES");
    }

    [Fact]
    public void BreaksNoInkRuleWithAnyNumberOfEatersFirstTimeOrNot()
    {
        foreach (var model in new[]
        {
            Book(), AllKnown(), Of(Sam(null)), Of(Sam(null), Lee(null)), Of(Sam(null), Lee(null), Robin(null), Juniper(null)),
            Of(Sam(TasteVerdict.NeverAgain), Lee(TasteVerdict.NeverAgain)),
            Of(
                Sam(null) with { Dish = "Roasted tomato and white bean soup with crusty bread" },
                Lee(null) with { Dish = "Roasted tomato and white bean soup with crusty bread" },
                Robin(null) with { Dish = "Roasted tomato and white bean soup with crusty bread" },
                Juniper(null) with { Dish = "Roasted tomato and white bean soup with crusty bread" }),
        })
        {
            var page = Page(model);

            Assert.Empty(page.Violations());
        }
    }

    [Fact]
    public void RefusesWhatItCannotPrintPlainly()
    {
        Assert.Throws<ArgumentException>(() => Page(Of()));
        Assert.Throws<ArgumentException>(() => Page(Of(Sam(), Lee(), Robin(), Juniper(), Sam())));
        Assert.Throws<ArgumentException>(() => Page(Of(Sam() with { Name = "Bartholomew-Jones" })));
        Assert.Throws<ArgumentException>(() => Page(Of(Sam() with { Dish = " " })));
        Assert.Throws<ArgumentException>(() => Page(new RecipeRatingModel(" ", Tuesday, [Sam()])));
    }

    [Fact]
    public void WordsKeepInsideTheirColumnsByRealGlyphWidths()
    {
        using var strong = new SKFont(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Body);
        using var small = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary);
        using var choice = new SKFont(EmbeddedTypefaces.SerifRegular, PaperTypeScale.Heading);
        var long1 = Of(Sam(null) with { Name = "Maximilian", Dish = "Roasted tomato and white bean soup with crusty bread" }, Lee(null));
        var page = Page(long1);

        foreach (var item in page.Printed.Where(i => i.Name.Contains("-name", StringComparison.Ordinal) || i.Name.Contains("-dish-", StringComparison.Ordinal)))
        {
            var font = item.Role == PrintedRole.Strong ? strong : small;
            Assert.True(font.MeasureText(item.Text!) <= item.Bounds.Width, $"'{item.Text}' is {font.MeasureText(item.Text!):0.0} points in an {item.Bounds.Width:0} point column.");
            Assert.True(item.Bounds.Right <= Lane(page, 1, 1).Bounds.Left - InkTargets.MinimumClearance + 0.01f, item.Text);
        }

        foreach (var lane in Enumerable.Range(1, 3).Select(n => Lane(Page(), 1, n)))
        {
            Assert.True(choice.MeasureText(lane.Meaning) <= lane.Bounds.Width - 2f * 8f, $"'{lane.Meaning}' is {choice.MeasureText(lane.Meaning):0.0} wide in a {lane.Bounds.Width:0} lane.");
        }

        var firstTime = Item(Page(), "rate-4-first-time");
        using var label = new SKFont(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Label);
        Assert.True(label.MeasureText(firstTime.Text!) <= firstTime.Bounds.Width, firstTime.Text);
    }

    [Fact]
    public void AnUnderlineIsAsWideAsTheWordItRules()
    {
        using var choice = new SKFont(EmbeddedTypefaces.SerifRegular, PaperTypeScale.Heading);
        foreach (var (verdict, lane) in new[] { (TasteVerdict.Loved, 1), (TasteVerdict.Fine, 2), (TasteVerdict.NeverAgain, 3) })
        {
            var underline = Item(Page(Of(Sam(verdict))), "rate-1-underline");
            var word = choice.MeasureText(RecipeIds.RatingWords[lane - 1]);

            Assert.InRange(underline.Bounds.Width, word * 0.9f, word * 1.1f);
        }
    }

    [Fact]
    public void RendersTheWholePageToAnOneSheetPdfWithItsWords()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Page()]));
        var text = WorkoutCoverTests.TextOf(document.GetPage(1));

        Assert.Equal(1, document.NumberOfPages);
        Assert.Contains("Who liked it?", text);
        Assert.Contains("Never again", text);
        Assert.Contains("FIRST TIME · PLEASE CIRCLE", text);
        Assert.Contains("We didn’t cook this tonight", text);
        Assert.Contains("4 of 4", text);
    }

    [Fact]
    public void TheFooterSitsWhereTheRecipesOwnFooterDoes()
    {
        var pages = RecipeWithRating.Build(RecipeSamples.Timed(), Book());

        Assert.Equal(pages[0].Printed.Single(i => i.Name == "footer").Bounds, pages[^1].Printed.Single(i => i.Name == "footer").Bounds);
        Assert.Equal(pages[0].Printed.Single(i => i.Name == "page-number").Bounds, pages[^1].Printed.Single(i => i.Name == "page-number").Bounds);
        Assert.Equal(pages[0].Printed.Single(i => i.Name == "kicker").Bounds.Left, pages[^1].Printed.Single(i => i.Name == "kicker").Bounds.Left);
        Assert.Equal(pages[0].Printed.Single(i => i.Name == "footer").Text, pages[^1].Printed.Single(i => i.Name == "footer").Text);
    }

    [Fact]
    public void TheFooterCountsTheWholeRecipeDocument()
    {
        var pages = RecipeWithRating.Build(RecipeSamples.Timed(), Book());

        Assert.Equal(4, pages.Count);
        Assert.Equal("Who liked it?", pages[^1].Title);
        Assert.All(pages, p => Assert.EndsWith(" of 4", p.Printed.Single(i => i.Name == "page-number").Text, StringComparison.Ordinal));
        Assert.Equal("4 of 4", pages[^1].Printed.Single(i => i.Name == "page-number").Text);
        Assert.All(pages, p => Assert.Empty(p.Violations()));
    }

    [Fact]
    public void TheRatingPageFollowsAFlowingRecipeAndCountsItself()
    {
        var many = Enumerable.Range(1, 20).Select(i => new RecipeIngredient(i <= 10 ? "Main" : "Then", "1 tsp", $"spice number {i}")).ToList();
        var pages = RecipeWithRating.Build(RecipeSamples.Timed(ingredients: many), Book());

        Assert.True(pages.Count > 4);
        Assert.Equal($"{pages.Count} of {pages.Count}", pages[^1].Printed.Single(i => i.Name == "page-number").Text);
        Assert.All(pages, p => Assert.EndsWith($" of {pages.Count}", p.Printed.Single(i => i.Name == "page-number").Text, StringComparison.Ordinal));
    }

    [Fact]
    public void ARatingPageForAnotherCookOrDayIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RecipeWithRating.Build(RecipeSamples.Timed(), Book() with { Cook = "Sam" }));
        Assert.Throws<ArgumentException>(() => RecipeWithRating.Build(RecipeSamples.Timed(), Book() with { Day = Tuesday.AddDays(1) }));
        Assert.Equal(4, RecipeWithRating.Build(RecipeSamples.Timed(), Book() with { Cook = " lee " }).Count);
    }

    [Fact]
    public void ARecipeThatAlreadyExpectsOtherTrailingPagesIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RecipeWithRating.Build(RecipeSamples.Timed() with { TrailingPages = 2 }, Book()));
        Assert.Throws<ArgumentException>(() => RecipeWithRating.Build(RecipeSamples.Timed() with { TrailingPages = 0 }, Book()));
    }

    private static (float, float, float, float) Dims(InkTarget t) => (t.Bounds.Left, t.Bounds.Top, t.Bounds.Width, t.Bounds.Height);

    private static float Baseline(InkPageLayout page, string name) => BaselineOf(Item(page, name));

    // The baseline is the item's bottom less its role's descent; the draft's table is internal, so read it the way the painter does.
    private static float BaselineOf(PrintedItem item) => item.Bounds.Bottom - item.Role switch
    {
        PrintedRole.Heading or PrintedRole.Choice => 4.5f,
        PrintedRole.Small => 4.2f,
        PrintedRole.Label => 3.9f,
        _ => 4.8f,
    };
}
