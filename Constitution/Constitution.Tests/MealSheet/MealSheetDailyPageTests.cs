using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using Storyworthtelling.Paper;

namespace Constitution.Tests.MealSheet;

/// <summary>C-2.4.2: pages 2 and 3 of the weekly meal sheet (breakfast and lunch, then lunchboxes), held to book p.7 by reading the rendered PDF back.</summary>
public class MealSheetDailyPageTests
{
    private static readonly string[] WeekdayBreakfast = [MealSheetIds.BreakfastYes(1), MealSheetIds.BreakfastYes(2), MealSheetIds.BreakfastYes(3)];

    private static readonly string[] BoxYes =
    [
        MealSheetIds.BoxSandwichYes, MealSheetIds.BoxFruitYes, MealSheetIds.BoxSnackYes, MealSheetIds.SnackFruitYes, MealSheetIds.SnackBarYes,
    ];

    private static IReadOnlyList<InkPageLayout> Pages(MealSheetDailyMeals? daily = null, int pages = 5) =>
        MealSheetDocument.Build(MealSheetSamples.Model(daily: daily ?? MealSheetSamples.Daily(), pages: pages));

    private static InkPageLayout Breakfast(MealSheetDailyMeals? daily = null) => Pages(daily)[1];

    private static InkPageLayout Box(MealSheetDailyMeals? daily = null) => Pages(daily)[2];

    private static string Rendered(InkPageLayout page)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        return WorkoutCoverTests.TextOf(document.GetPage(1));
    }

    private static IEnumerable<string> RowIds(InkPageLayout page) => page.Map.Targets.Select(t => t.Id);

    [Fact]
    public void AMealSheetWithoutDailyMealsIsOnePage() =>
        Assert.Single(MealSheetDocument.Build(MealSheetSamples.Model()));

    [Fact]
    public void WithDailyMealsItIsThreePages()
    {
        var pages = Pages();

        Assert.Equal(["Dinners", "Breakfast and lunch", "Lunchboxes"], pages.Select(p => p.Title));
    }

    [Fact]
    public void WeekendsArePlanned()
    {
        var page = Breakfast();
        var text = Rendered(page);

        Assert.Contains("WEEKEND · SAT–SUN", text);
        Assert.Contains("Banana pancakes, everyone", text);
        Assert.Contains("Cheese quesadillas, salsa, fruit", text);
        Assert.Equal(["Breakfast", "Lunch"], page.Printed.Where(i => i.Name is "weekend-breakfast-person" or "weekend-lunch-person").Select(i => i.Text));
        Assert.NotNull(page.Map.TryGet(MealSheetIds.WeekendBreakfastReason));
        Assert.NotNull(page.Map.TryGet(MealSheetIds.WeekendLunchYes));
    }

    [Fact]
    public void EveryLineIsApprovable()
    {
        foreach (var page in new[] { Breakfast(), Box() })
        {
            var yesIds = page.Map.Targets.Where(t => t.Kind == InkTargetKind.StandardTick).Select(t => t.Id).ToList();
            Assert.NotEmpty(yesIds);

            foreach (var id in yesIds)
            {
                var yes = page.Map.ById(id);
                var reason = page.Map.ById(id[..^"yes".Length] + "why");

                Assert.Equal(InkTargetKind.NoBecause, reason.Kind);
                Assert.Equal(yes.Bounds.CentreY, reason.Bounds.CentreY, 0.01);
                Assert.Equal(409f, reason.Bounds.Right, 0.01);
                Assert.True(reason.Bounds.Left > yes.Bounds.Right + 150f, "the reason field is at the other end of the row from YES");
                Assert.Single(page.Printed, i => i.OwnerId == id && i.Text == "YES");
                Assert.Single(page.Printed, i => i.OwnerId == reason.Id && i.Text == "No, because");
            }
        }

        Assert.Equal(14, Breakfast().Map.Targets.Count);
        Assert.Equal(10, Box().Map.Targets.Count);
    }

    [Fact]
    public void LunchboxesHaveTheirOwnPage()
    {
        var page = Box();
        var text = Rendered(page);

        Assert.Equal(BoxYes, page.Map.Targets.Where(t => t.Kind == InkTargetKind.StandardTick).Select(t => t.Id));
        Assert.Contains("IN THE BOX", text);
        Assert.Contains("SEPARATE SNACK", text);
        Assert.Contains("For the children, Monday to Friday. Nothing needs heating.", text);
        foreach (var words in new[] { "Sandwich", "Sunflower butter and jam", "Clementine", "Cheese stick", "Fruit or veg", "Cucumber slices", "Oat granola bar" })
        {
            Assert.Contains(words, text);
        }

        Assert.DoesNotContain("Overnight oats", text);
    }

    [Fact]
    public void WeekdayLinesCarryTheirPeople()
    {
        var page = Breakfast();
        var text = Rendered(page);

        Assert.Contains("WEEKDAY BREAKFAST · MON–FRI", text);
        Assert.Contains("WEEKDAY LUNCH · MON–FRI", text);
        Assert.Contains("Easy, uncooked weekday prep. Weekends are planned the same way.", text);
        Assert.Equal(WeekdayBreakfast, RowIds(page).Where(id => id.StartsWith("breakfast-", StringComparison.Ordinal) && id.EndsWith("-yes", StringComparison.Ordinal)));
        Assert.Equal(
            ["lunch-1-why", "lunch-1-yes", "lunch-2-why", "lunch-2-yes"],
            RowIds(page).Where(id => id.StartsWith("lunch-", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        foreach (var name in new[] { "Sam", "Lee", "Kids" })
        {
            Assert.Contains(name, page.Printed.Where(i => i.Name.EndsWith("-person", StringComparison.Ordinal)).Select(i => i.Text));
        }
    }

    [Fact]
    public void RowsRunDownEachPageInOrder()
    {
        foreach (var page in new[] { Breakfast(), Box() })
        {
            var tops = page.Map.Targets.Where(t => t.Kind == InkTargetKind.StandardTick).Select(t => t.Bounds.Top).ToList();

            Assert.Equal(tops.OrderBy(t => t), tops);
            Assert.All(tops.Zip(tops.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 48f));
        }
    }

    [Fact]
    public void BothPagesHaveNoInkViolations()
    {
        Assert.Empty(Breakfast().Violations());
        Assert.Empty(Box().Violations());
    }

    [Fact]
    public void TheKickersAndFootersCountTheSheet()
    {
        var pages = Pages();

        Assert.Contains("WEEKLY MEAL SHEET · 2 OF 5", Rendered(pages[1]));
        Assert.Contains("WEEKLY MEAL SHEET · 3 OF 5", Rendered(pages[2]));
        Assert.Equal("2 of 5", pages[1].Printed.Single(i => i.Name == "page-number").Text);
        Assert.Equal("3 of 5", pages[2].Printed.Single(i => i.Name == "page-number").Text);
        Assert.Contains("Constitution · Meals · week of Nov 15 · both tablets", Rendered(pages[1]));
        Assert.Equal("3 of 6", Pages(pages: 6)[2].Printed.Single(i => i.Name == "page-number").Text);
        Assert.Equal("1 of 6", Pages(pages: 6)[0].Printed.Single(i => i.Name == "page-number").Text);
    }

    [Fact]
    public void TheDeadlineLineStaysOnTheDinners()
    {
        var pages = Pages();

        Assert.Contains("Anything untouched is approved Sunday morning.", Rendered(pages[0]));
        Assert.DoesNotContain("Anything untouched", Rendered(pages[1]));
    }

    [Fact]
    public void RendersThreePagesAtTheTabletSize()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(Pages()));

        Assert.Equal(3, document.NumberOfPages);
        Assert.Equal((445d, 594d), (Math.Round(document.GetPage(3).Width), Math.Round(document.GetPage(3).Height)));
    }

    [Fact]
    public void OnlyTheHeaderRuleIsColoured()
    {
        string[] neutral =
        [
            PaperColorTokens.Paper, PaperColorTokens.PaperBright, PaperColorTokens.PaperDeep, PaperColorTokens.PaperShade,
            PaperColorTokens.Hairline, PaperColorTokens.Ink, PaperColorTokens.InkSoft, PaperColorTokens.InkMuted,
        ];
        var header = PdfReadBack.Rgb(AppIdentityTokens.ConstitutionDocument);

        foreach (var page in new[] { Breakfast(), Box() })
        {
            using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
            var coloured = PdfReadBack.PaintedColours(document.GetPage(1))
                .Where(c => !neutral.Any(hex => PdfReadBack.SameColour((c.R, c.G, c.B), PdfReadBack.Rgb(hex)))
                    && !(c.R > 0.99 && c.G > 0.99 && c.B > 0.99)).ToList();

            Assert.NotEmpty(coloured);
            Assert.All(coloured, c => Assert.True(PdfReadBack.SameColour((c.R, c.G, c.B), header), "only the header rule is coloured"));
        }
    }

    [Fact]
    public void TheShadedFieldIsDrawnWhereTheTargetIs()
    {
        var muted = PdfReadBack.Rgb(PaperColorTokens.InkMuted);

        foreach (var page in new[] { Breakfast(), Box() })
        {
            using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
            var paths = PdfReadBack.Paths(document.GetPage(1));

            foreach (var target in page.Map.Targets)
            {
                Assert.Contains(paths, p => p.Stroked && PdfReadBack.Same(p.Bounds, target.Bounds) && PdfReadBack.SameColour(p.Stroke, muted));
                if (target.Kind == InkTargetKind.NoBecause)
                {
                    Assert.Contains(paths, p => p.Filled && PdfReadBack.Same(p.Bounds, target.Bounds));
                }
            }
        }
    }

    [Fact]
    public void LongItemsWrapInsideTheirColumn()
    {
        var daily = MealSheetSamples.Daily() with
        {
            WeekdayBreakfasts = [new("Sam", "Overnight oats with blueberries"), new("Lee", "Yogurt, granola, banana"), new("Kids", "Toast, peanut butter, apple")],
            WeekendLunch = "Cheese quesadillas, salsa and fruit",
        };
        var page = Breakfast(daily);
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var first = document.GetPage(1);

        var lines = page.Printed.Where(i => i.Name.EndsWith("-line", StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count(l => l.Name == "breakfast-1-line") >= 2, "the long item wraps");
        foreach (var item in lines)
        {
            var drawn = first.GetWords()
                .Select(w => PdfReadBack.Flip(first, w.BoundingBox))
                .Where(b => b.CentreY > item.Bounds.Top && b.CentreY < item.Bounds.Bottom && b.Left >= item.Bounds.Left - 0.5f && b.Left < item.Bounds.Right)
                .ToList();

            Assert.NotEmpty(drawn);
            Assert.True(drawn.Max(b => b.Right) <= item.Bounds.Right + 0.5f, $"'{item.Text}' runs past its column ({drawn.Max(b => b.Right):0.0} > {item.Bounds.Right:0.0})");
        }

        foreach (var field in page.Map.Targets.Where(t => t.Kind == InkTargetKind.NoBecause))
        {
            Assert.DoesNotContain(lines, l => l.Bounds.Right > field.Bounds.Left - InkTargets.MinimumClearance - 64f + 0.5f);
        }

        Assert.Empty(page.Violations());
    }

    [Fact]
    public void AnItemThatCannotFitItsRowIsRefused()
    {
        var daily = MealSheetSamples.Daily() with { WeekdayLunches = [new("Sam", string.Join(' ', Enumerable.Repeat("extraordinarily elaborate", 8)))] };

        var error = Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(daily: daily)));
        Assert.Contains("Sam", error.Message);
        Assert.Contains("lunch", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AThreeLineItemIsRefused()
    {
        var daily = MealSheetSamples.Daily() with { WeekendBreakfast = "Banana pancakes with maple syrup and sliced strawberries" };

        var error = Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(daily: daily)));
        Assert.Contains("takes 3 lines", error.Message);
    }

    [Fact]
    public void ASixthLineInAGroupIsRefused()
    {
        var six = Enumerable.Range(1, 6).Select(i => new MealSheetLine("Guest " + i, "Toast")).ToList();
        var daily = MealSheetSamples.Daily() with { WeekdayBreakfasts = six };

        var error = Assert.ThrowsAny<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(daily: daily)));
        Assert.Contains("at most", error.Message);
    }

    [Fact]
    public void TooManyLinesForOnePageAreRefused()
    {
        var many = Enumerable.Range(1, 4).Select(i => new MealSheetLine("Guest " + i, "Toast")).ToList();
        var daily = MealSheetSamples.Daily() with { WeekdayBreakfasts = many, WeekdayLunches = many };

        var error = Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(daily: daily)));
        Assert.Contains("room", error.Message);
    }

    [Fact]
    public void ABlankLineIsRefused()
    {
        var blankWeekend = MealSheetSamples.Daily() with { WeekendLunch = "  " };
        var blankPart = MealSheetSamples.Daily() with { Lunchbox = MealSheetSamples.Daily().Lunchbox with { Snack = "" } };
        var noBreakfasts = MealSheetSamples.Daily() with { WeekdayBreakfasts = [] };

        Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(daily: blankWeekend)));
        Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(daily: blankPart)));
        Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(daily: noBreakfasts)));
    }

    [Fact]
    public void AShortSheetIsRefused() =>
        Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(daily: MealSheetSamples.Daily(), pages: 2)));

    [Fact]
    public void TheNewIdsAreFrozenLiterals()
    {
        Assert.Equal(
            ["breakfast-1-yes", "breakfast-2-yes", "breakfast-3-yes", "breakfast-4-yes", "breakfast-5-yes"],
            Enumerable.Range(1, 5).Select(MealSheetIds.BreakfastYes));
        Assert.Equal(
            ["breakfast-1-why", "breakfast-2-why", "breakfast-3-why", "breakfast-4-why", "breakfast-5-why"],
            Enumerable.Range(1, 5).Select(MealSheetIds.BreakfastReason));
        Assert.Equal(
            ["lunch-1-yes", "lunch-2-yes", "lunch-3-yes", "lunch-4-yes", "lunch-5-yes"],
            Enumerable.Range(1, 5).Select(MealSheetIds.LunchYes));
        Assert.Equal(
            ["lunch-1-why", "lunch-2-why", "lunch-3-why", "lunch-4-why", "lunch-5-why"],
            Enumerable.Range(1, 5).Select(MealSheetIds.LunchReason));
        Assert.Equal(
            ["weekend-breakfast-yes", "weekend-breakfast-why", "weekend-lunch-yes", "weekend-lunch-why"],
            [MealSheetIds.WeekendBreakfastYes, MealSheetIds.WeekendBreakfastReason, MealSheetIds.WeekendLunchYes, MealSheetIds.WeekendLunchReason]);
        Assert.Equal(
            [
                "box-sandwich-yes", "box-sandwich-why", "box-fruit-yes", "box-fruit-why", "box-snack-yes", "box-snack-why",
                "snack-fruit-yes", "snack-fruit-why", "snack-bar-yes", "snack-bar-why",
            ],
            [
                MealSheetIds.BoxSandwichYes, MealSheetIds.BoxSandwichReason, MealSheetIds.BoxFruitYes, MealSheetIds.BoxFruitReason,
                MealSheetIds.BoxSnackYes, MealSheetIds.BoxSnackReason, MealSheetIds.SnackFruitYes, MealSheetIds.SnackFruitReason,
                MealSheetIds.SnackBarYes, MealSheetIds.SnackBarReason,
            ]);
        Assert.Throws<ArgumentOutOfRangeException>(() => MealSheetIds.BreakfastYes(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MealSheetIds.LunchReason(6));
    }
}
