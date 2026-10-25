using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Domain.Ink;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using Constitution.Tests.Groceries;
using Constitution.Tests.Ink;
using Storyworthtelling.Paper;

namespace Constitution.Tests.MealSheet;

/// <summary>C-2.4.3: the groceries page and the staples to tick, held to book p.7 by reading the rendered PDF back. Every dish and figure is invented.</summary>
public class GroceryPageTests
{
    private static string Rendered(params InkPageLayout[] pages)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));
        return string.Join('\n', Enumerable.Range(1, document.NumberOfPages).Select(n => WorkoutCoverTests.TextOf(document.GetPage(n))));
    }

    private static IReadOnlyList<InkPageLayout> GroceryPages(MealSheetModel model) => MealSheetDocument.Build(model).Skip(3).ToList();

    private static IEnumerable<InkTarget> Ticks(InkPageLayout page) => page.Map.Targets.Where(t => t.Kind == InkTargetKind.StandardTick);

    private static MealSheetGroceries Long() => MealSheetGrocerySamples.Groceries(
        pantry: MealSheetGrocerySamples.StandingPantry(),
        list: GroceryList.From(
            GroceryData.Cooking((0, GroceryData.Chili), (1, GroceryData.Chicken), (2, GroceryData.Pasta), (3, LongCard("a")), (4, LongCard("b")), (5, LongCard("c"))),
            [.. GroceryData.Cards, LongCard("a"), LongCard("b"), LongCard("c")],
            MealSheetGrocerySamples.StandingPantry(), 4));

    private static Constitution.Domain.Recipes.Recipe LongCard(string suffix) => GroceryData.Card(
        "long-" + suffix, "Long " + suffix, 4,
        [.. Enumerable.Range(1, 22).Select(n => GroceryData.Item("1", "bunch", $"wild garden herb number {n}{suffix} for the long soup"))]);

    [Fact]
    public void TheGroceryPageFollowsTheLunchboxes()
    {
        var pages = MealSheetGrocerySamples.Pages();

        Assert.Equal(["Dinners", "Breakfast and lunch", "Lunchboxes", "Groceries"], pages.Select(p => p.Title));
        Assert.Contains("WEEKLY MEAL SHEET · 4 OF 5", Rendered(pages[3]));
        Assert.Equal("4 of 5", pages[3].Printed.Single(i => i.Name == "page-number").Text);
        Assert.Contains("Constitution · Meals · week of Nov 15 · both tablets", Rendered(pages[3]));
    }

    [Fact]
    public void ASheetWithoutGroceriesIsAsItWas() =>
        Assert.Equal(3, MealSheetDocument.Build(MealSheetSamples.Model(daily: MealSheetSamples.Daily())).Count);

    [Fact]
    public void GroceriesPrintByStoreSection()
    {
        var page = MealSheetGrocerySamples.Page();
        var text = Rendered(page);
        var headings = page.Printed.Where(i => i.Name.EndsWith("-aisle", StringComparison.Ordinal)).ToList();
        var expected = MealSheetGrocerySamples.Groceries().List.Sections.Select(s => Aisles.Label(s.Aisle).ToUpperInvariant()).ToList();

        Assert.Equal(expected, headings.Select(h => h.Text));
        Assert.Equal(headings.OrderBy(h => h.Bounds.Top), headings);
        Assert.All(expected, heading => Assert.Contains(heading, text));
    }

    [Fact]
    public void EveryLineOfTheListPrintsWithItsQuantity()
    {
        var page = MealSheetGrocerySamples.Page();
        var printed = string.Join(' ', page.Printed.Where(i => i.Name.StartsWith("list-", StringComparison.Ordinal)).Select(i => i.Text));

        foreach (var line in MealSheetGrocerySamples.Groceries().List.Lines)
        {
            Assert.Contains(string.IsNullOrEmpty(line.Quantity) ? line.Name : $"{line.Name} ({line.Quantity})", printed);
        }

        Assert.Contains("black beans (2 cans)", printed);
    }

    [Fact]
    public void TheListIsThePreviewOnly()
    {
        var text = Rendered(MealSheetGrocerySamples.Page());

        Assert.Contains("A preview only", text);
        Assert.Contains("Keep", text);
    }

    [Fact]
    public void AnEmptyListSaysSo()
    {
        var empty = MealSheetGrocerySamples.Groceries(list: GroceryList.From(Constitution.Domain.Meals.MealWeek.Planned(MealSheetSamples.Sunday, Enumerable.Range(0, 7).Select(n => Constitution.Domain.Meals.DinnerChoice.EatOut(MealSheetSamples.Sunday.AddDays(n), "Out all week."))), [], Pantry.Empty, 4));
        var page = MealSheetGrocerySamples.Page(MealSheetGrocerySamples.Model(empty, pages: 5) with { Groceries = empty with { Pantry = Pantry.Empty } });

        Assert.Contains("Nothing to buy this week.", Rendered(page));
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void OverBudgetIsAFlagInWords()
    {
        var groceries = MealSheetGrocerySamples.Groceries(MealSheetGrocerySamples.Over());
        var page = MealSheetGrocerySamples.Page(MealSheetGrocerySamples.Model(groceries));
        var text = Rendered(page);

        Assert.Contains("OVER BUDGET", text);
        Assert.Contains("34.50 over this month", text.Replace('\n', ' '));
        Assert.Contains("Say no to a dinner to bring it down.", text.Replace('\n', ' '));

        var rules = page.Printed.Where(i => i.Name.StartsWith("budget-flag-", StringComparison.Ordinal)).ToList();
        Assert.Equal(["budget-flag-bottom", "budget-flag-left", "budget-flag-right", "budget-flag-top"], rules.Select(r => r.Name).Order());
        var top = rules.Single(r => r.Name == "budget-flag-top").Bounds;
        var bottom = rules.Single(r => r.Name == "budget-flag-bottom").Bounds;
        Assert.All(
            page.Printed.Where(i => i.Name.StartsWith("budget-", StringComparison.Ordinal) && i.Role is not PrintedRole.Rule),
            word => Assert.True(word.Bounds.Top >= top.Top && word.Bounds.Bottom <= bottom.Top, $"{word.Name} is inside the frame"));
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void WithinBudgetIsAPlainLineAndNoFrame()
    {
        var page = MealSheetGrocerySamples.Page(MealSheetGrocerySamples.Model(MealSheetGrocerySamples.Groceries(MealSheetGrocerySamples.Within())));
        var text = Rendered(page).Replace('\n', ' ');

        Assert.DoesNotContain(page.Printed, i => i.Name.StartsWith("budget-flag-", StringComparison.Ordinal));
        Assert.DoesNotContain("OVER BUDGET", text);
        Assert.Contains("409.75 would be left of this month", text);
    }

    [Fact]
    public void NoBudgetYetPrintsNoFlagAndNoLine()
    {
        var page = MealSheetGrocerySamples.Page(MealSheetGrocerySamples.Model(MealSheetGrocerySamples.Groceries(MealSheetGrocerySamples.Unknown())));
        var text = Rendered(page);

        Assert.DoesNotContain(page.Printed, i => i.Name.StartsWith("budget-", StringComparison.Ordinal));
        Assert.DoesNotContain("budget", text.ToLowerInvariant().Replace("budget-", string.Empty));
    }

    [Fact]
    public void StaplesHaveNoYesNo()
    {
        var page = MealSheetGrocerySamples.Page();
        var text = Rendered(page);

        Assert.DoesNotContain("YES", text);
        Assert.DoesNotContain("No, because", text);
        Assert.DoesNotContain(page.Map.Targets, t => t.Kind == InkTargetKind.NoBecause);
        Assert.Contains("STAPLES · TICK WHAT WE NEED MORE OF", text);
        Assert.Contains("Something we always keep that isn’t listed? Write it on a line.", text.Replace('\n', ' '));
    }

    [Fact]
    public void EachStapleIsAPlainBoxWithItsName()
    {
        var page = MealSheetGrocerySamples.Page();
        var names = MealSheetGrocerySamples.StandingPantry().Standing.Select(s => s.Name).Order().ToList();

        Assert.Equal(8, Ticks(page).Count());
        foreach (var tick in Ticks(page))
        {
            Assert.Equal(18f, tick.Bounds.Width);
            var name = page.Printed.Single(i => i.OwnerId == tick.Id && i.Text is not null);
            Assert.True(name.Bounds.Left >= tick.Bounds.Right + InkTargets.MinimumClearance - 0.01f);
            Assert.Contains(name.Text!.ToLowerInvariant(), names.Select(n => n.ToLowerInvariant()));
        }
    }

    [Fact]
    public void StapleIdsAreSlotBasedAndAisleGrouped()
    {
        var ids = Ticks(MealSheetGrocerySamples.Page()).Select(t => t.Id).ToList();

        Assert.Equal(
            ["staple-produce-1", "staple-dairy-eggs-1", "staple-dairy-eggs-2", "staple-bakery-1", "staple-canned-jarred-1", "staple-canned-jarred-2", "staple-dry-goods-1", "staple-dry-goods-2"],
            ids);
    }

    [Fact]
    public void BlankLinesAreProvidedForNewStaples()
    {
        var page = MealSheetGrocerySamples.Page();
        var lines = page.Map.Targets.Where(t => t.Kind == InkTargetKind.NumberBox).ToList();

        Assert.Equal(["staple-new-1", "staple-new-2"], lines.Select(l => l.Id));
        Assert.All(lines, l => Assert.True(l.Bounds.Width >= 200f && l.Bounds.Height >= InkTargets.WriteLineHeight));
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void ANewWordBecomesAStaple()
    {
        var model = MealSheetGrocerySamples.Model();
        var document = Constitution.Domain.ReadBack.ExpectedDocument.ForMealSheet(model, "Sam");
        var page = document.Pages.Single(p => p.Number == 4);
        var resolver = new InkResolver(page.Readout);

        var reads = page.Readout.Map.Targets.Select(t => t.Id == "staple-new-2" ? InkFixture.Ink(t.Id, "capers") : InkFixture.Empty(t.Id)).ToList();
        var resolved = resolver.Resolve(InkPageRead.Read(reads), page.Rules, MealSheetSamples.Deadline);

        var needed = Assert.Single(resolved.OfType<InkResolution.Needed>());
        Assert.Equal("staple-new-2", needed.SubjectId);
        Assert.Equal("capers", needed.Text);
        Assert.Equal(9, resolved.OfType<InkResolution.NoNeed>().Count());
    }

    [Fact]
    public void ATickedStapleIsNeededAndABlankOneIsNot()
    {
        var page = Constitution.Domain.ReadBack.ExpectedDocument.ForMealSheet(MealSheetGrocerySamples.Model(), "Sam").Pages.Single(p => p.Number == 4);
        var resolver = new InkResolver(page.Readout);
        var reads = page.Readout.Map.Targets.Select(t => t.Id == "staple-dry-goods-1" ? InkFixture.Ink(t.Id) : InkFixture.Empty(t.Id)).ToList();

        var resolved = resolver.Resolve(InkPageRead.Read(reads), page.Rules, MealSheetSamples.Deadline);

        var needed = Assert.Single(resolved.OfType<InkResolution.Needed>());
        Assert.Equal("staple-dry-goods-1", needed.SubjectId);
        Assert.Null(needed.Text);
        Assert.Equal(9, resolved.OfType<InkResolution.NoNeed>().Count());
    }

    [Fact]
    public void ThePageHasNoInkViolations() =>
        Assert.Empty(MealSheetGrocerySamples.Page().Violations());

    [Fact]
    public void OnlyTheHeaderRuleIsInColour()
    {
        var pages = GroceryPages(MealSheetGrocerySamples.Model());
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));
        var header = PdfReadBack.Rgb(AppIdentityTokens.ConstitutionDocument);
        string[] neutral =
        [
            PaperColorTokens.Paper, PaperColorTokens.PaperBright, PaperColorTokens.PaperDeep, PaperColorTokens.PaperShade,
            PaperColorTokens.Hairline, PaperColorTokens.Ink, PaperColorTokens.InkSoft, PaperColorTokens.InkMuted,
        ];
        var coloured = PdfReadBack.PaintedColours(document.GetPage(1))
            .Where(c => !neutral.Any(hex => PdfReadBack.SameColour((c.R, c.G, c.B), PdfReadBack.Rgb(hex)))
                && !(c.R > 0.99 && c.G > 0.99 && c.B > 0.99)).ToList();

        Assert.NotEmpty(coloured);
        Assert.All(coloured, c => Assert.True(PdfReadBack.SameColour((c.R, c.G, c.B), header), "only the header rule is coloured"));
    }

    [Fact]
    public void TheBoxesAreOutlinedInMutedInk()
    {
        var page = MealSheetGrocerySamples.Page();
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var paths = PdfReadBack.Paths(document.GetPage(1));
        var muted = PdfReadBack.Rgb(PaperColorTokens.InkMuted);

        foreach (var target in page.Map.Targets)
        {
            Assert.Contains(paths, p => p.Stroked && PdfReadBack.Same(p.Bounds, target.Bounds) && PdfReadBack.SameColour(p.Stroke, muted));
        }
    }

    [Fact]
    public void LongLinesWrapInsideTheirMeasure()
    {
        var long1 = MealSheetGrocerySamples.Groceries(
            pantry: MealSheetGrocerySamples.StandingPantry().With(new Staple("an extraordinarily well matured and slow aged balsamic vinegar of Modena", Aisle.CannedAndJarred, false)),
            list: GroceryList.From(
                GroceryData.Cooking((0, GroceryData.Card("odd", "Odd", 4, GroceryData.Item("3", "jars", "hand harvested wildflower honey from the northern hillside apiary"), GroceryData.Item("1", "tub", "unsweetened coconut yoghurt with live cultures")))),
                [GroceryData.Card("odd", "Odd", 4, GroceryData.Item("3", "jars", "hand harvested wildflower honey from the northern hillside apiary"), GroceryData.Item("1", "tub", "unsweetened coconut yoghurt with live cultures"))],
                MealSheetGrocerySamples.StandingPantry(), 4));
        long1 = long1 with { Pantry = long1.Pantry.With(new Staple("an extraordinarily well matured and slow aged balsamic vinegar of Modena", Aisle.CannedAndJarred, false)) };
        var page = MealSheetGrocerySamples.Page(MealSheetGrocerySamples.Model(long1));
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var first = document.GetPage(1);

        foreach (var item in page.Printed.Where(i => i.Text is not null && (i.Name.StartsWith("list-", StringComparison.Ordinal) || i.Name.StartsWith("staple-name", StringComparison.Ordinal) || i.Name.StartsWith("budget-", StringComparison.Ordinal))))
        {
            var drawn = first.GetWords().Select(w => PdfReadBack.Flip(first, w.BoundingBox))
                .Where(b => b.CentreY > item.Bounds.Top && b.CentreY < item.Bounds.Bottom && b.Left >= item.Bounds.Left - 0.5f && b.Left < item.Bounds.Right).ToList();

            Assert.NotEmpty(drawn);
            Assert.True(drawn.Max(b => b.Right) <= item.Bounds.Right + 0.5f, $"'{item.Text}' runs past its measure ({drawn.Max(b => b.Right):0.0} > {item.Bounds.Right:0.0})");
        }

        Assert.Empty(page.Violations());
    }

    [Fact]
    public void ALongListFlowsToAContinuationPage()
    {
        var model = MealSheetGrocerySamples.Model(Long());
        var pages = GroceryPages(model);

        Assert.True(pages.Count >= 2, "the list does not fit one page");
        Assert.Equal("Groceries", pages[0].Title);
        Assert.All(pages.Skip(1), p => Assert.Equal("Groceries, continued", p.Title));
        Assert.All(pages, p => Assert.Empty(p.Violations()));

        var all = MealSheetDocument.Build(model);
        Assert.Equal(3 + pages.Count, all.Count);
        var total = 5 + pages.Count - 1; // the sheet's declared five, plus the pages the list ran on to
        for (var index = 0; index < all.Count; index++)
        {
            Assert.Equal($"{index + 1} of {total}", all[index].Printed.Single(i => i.Name == "page-number").Text);
            Assert.Contains($"WEEKLY MEAL SHEET · {index + 1} OF {total}", Rendered(all[index]));
        }
    }

    [Fact]
    public void NothingIsLostOrRepeatedAcrossThePages()
    {
        var model = MealSheetGrocerySamples.Model(Long());
        var pages = GroceryPages(model);
        var printed = string.Join(' ', pages.SelectMany(p => p.Printed.Where(i => i.Name.StartsWith("list-", StringComparison.Ordinal))).Select(i => i.Text));

        foreach (var line in model.Groceries!.List.Lines)
        {
            var text = string.IsNullOrEmpty(line.Quantity) ? line.Name : $"{line.Name} ({line.Quantity})";
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(printed, System.Text.RegularExpressions.Regex.Escape(text)).Count);
        }

        var ids = pages.SelectMany(p => p.Map.Targets.Select(t => t.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(8, ids.Count(id => id.StartsWith("staple-", StringComparison.Ordinal) && !id.StartsWith("staple-new", StringComparison.Ordinal)));
        Assert.Equal(2, ids.Count(id => id.StartsWith("staple-new", StringComparison.Ordinal)));
    }

    [Fact]
    public void ManyLongStaplesFlowAndNeverOverlap()
    {
        var groceries = MealSheetGrocerySamples.Groceries(pantry: MealSheetGrocerySamples.ManyStaples(46));
        var model = MealSheetGrocerySamples.Model(groceries);
        var pages = GroceryPages(model);

        Assert.True(pages.Count >= 2);
        Assert.All(pages, p => Assert.Empty(p.Violations()));
        Assert.Equal(46, pages.Sum(p => Ticks(p).Count()));
        var text = Rendered(pages.ToArray());
        Assert.Contains("STAPLES · TICK WHAT WE NEED MORE OF", text);
    }

    [Fact]
    public void ASheetTooShortForTheGroceryPageIsRefused() =>
        Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetGrocerySamples.Model(pages: 3)));

    [Fact]
    public void TheIdsAreFrozenLiterals()
    {
        Assert.Equal("staple-produce-1", MealSheetIds.StapleNeed(Aisle.Produce, 1));
        Assert.Equal("staple-meat-fish-3", MealSheetIds.StapleNeed(Aisle.MeatAndFish, 3));
        Assert.Equal("staple-dairy-eggs-2", MealSheetIds.StapleNeed(Aisle.DairyAndEggs, 2));
        Assert.Equal("staple-baking-spices-12", MealSheetIds.StapleNeed(Aisle.BakingAndSpices, 12));
        Assert.Equal(["staple-new-1", "staple-new-2"], Enumerable.Range(1, 2).Select(MealSheetIds.NewStaple));
        Assert.Throws<ArgumentOutOfRangeException>(() => MealSheetIds.StapleNeed(Aisle.Produce, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MealSheetIds.NewStaple(3));
    }
}
