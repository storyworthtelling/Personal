using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using Storyworthtelling.Paper;

namespace Constitution.Tests.MealSheet;

/// <summary>C-2.4.1: the dinners page, held to book p.7 (treatment B) by reading the rendered PDF back.</summary>
public class MealSheetPageTests
{
    private static readonly DayOfWeek[] Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static string Rendered(InkPageLayout page)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        return WorkoutCoverTests.TextOf(document.GetPage(1));
    }

    private static IEnumerable<string> Texts(InkPageLayout page, PrintedRole role) =>
        page.Printed.Where(i => i.Role == role && i.Text is not null).Select(i => i.Text!);

    [Fact]
    public void DinnerRowsCarryBothEnds()
    {
        var page = MealSheetSamples.Page();

        foreach (var day in Week)
        {
            var yes = page.Map.ById(MealSheetIds.DinnerYes(day));
            var reason = page.Map.ById(MealSheetIds.DinnerReason(day));

            Assert.Equal(InkTargetKind.StandardTick, yes.Kind);
            Assert.Equal(InkTargetKind.NoBecause, reason.Kind);
            Assert.Equal(yes.Bounds.CentreY, reason.Bounds.CentreY, 0.01);
            Assert.True(reason.Bounds.Left > yes.Bounds.Right + 150f, "the reason field is at the other end of the row from YES");
            Assert.Equal(PageDraft409(), reason.Bounds.Right, 0.01);
        }

        Assert.Equal(14, page.Map.Targets.Count);
    }

    private static float PageDraft409() => 409f;

    [Fact]
    public void RowsRunDownThePageInWeekOrder()
    {
        var page = MealSheetSamples.Page();
        var tops = Week.Select(day => page.Map.ById(MealSheetIds.DinnerYes(day)).Bounds.Top).ToList();

        Assert.Equal(tops.OrderBy(t => t), tops);
        Assert.All(tops.Zip(tops.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 48f));
    }

    [Fact]
    public void ThePageHasNoInkViolations() =>
        Assert.Empty(MealSheetSamples.Page().Violations());

    [Fact]
    public void TheKickerTitleAndFooterAreAsDrawn()
    {
        var page = MealSheetSamples.Page();
        var text = Rendered(page);

        Assert.Equal("Dinners", page.Title);
        Assert.Contains("WEEKLY MEAL SHEET · 1 OF 5", text);
        Assert.Contains("Constitution · Meals · week of Nov 16 · both tablets", text);
        Assert.Equal("1 of 5", page.Printed.Single(i => i.Name == "page-number").Text);
    }

    [Fact]
    public void EveryRowNamesYesAndNoBecause()
    {
        var page = MealSheetSamples.Page();

        foreach (var day in Week)
        {
            var yes = page.Printed.Single(i => i.OwnerId == MealSheetIds.DinnerYes(day) && i.Text == "YES");
            var no = page.Printed.Single(i => i.OwnerId == MealSheetIds.DinnerReason(day) && i.Text == "No, because");

            Assert.True(yes.Bounds.Top > page.Map.ById(MealSheetIds.DinnerYes(day)).Bounds.Bottom, "YES is under its box");
            Assert.True(no.Bounds.Right <= page.Map.ById(MealSheetIds.DinnerReason(day)).Bounds.Left, "No, because stands at the field's left");
        }

        var text = Rendered(page);
        Assert.Equal(7 + 1, text.Split("YES").Length - 1); // seven rows, and the tick line's own YES
        Assert.Equal(7, text.Split("No, because").Length - 1);
    }

    [Fact]
    public void MarkersAreWordsNotColours()
    {
        var page = MealSheetSamples.Page();
        var words = Texts(page, PrintedRole.Label).ToList();

        Assert.Contains("SLOW COOKER", words);
        Assert.Contains("NEW RECIPE", words);
        Assert.Contains("EATING OUT", words);
        Assert.Equal(3, words.Count(w => w is "SLOW COOKER" or "NEW RECIPE" or "EATING OUT"));

        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
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
    public void TheCookAndTheCookBlockTimeAreStated()
    {
        var text = Rendered(MealSheetSamples.Page());

        Assert.Contains("Sam cooks", text);
        Assert.Contains("Lee cooks", text);
        Assert.Contains("Sam starts it 7:40 am", text);
        Assert.Contains("Leave 5:45 pm", text);
        Assert.Contains("Kids: buttered noodles", text);
    }

    [Fact]
    public void TheDeadlineLineSaysWhenSilenceApproves()
    {
        var page = MealSheetSamples.Page();

        Assert.Contains("Anything untouched is approved Sunday morning.", Rendered(page));
        Assert.Contains("Tick YES to approve, or write after NO, BECAUSE.", Rendered(page));

        var evening = MealSheetSamples.Page(MealSheetSamples.Model(deadline: new DateTimeOffset(2026, 11, 22, 19, 0, 0, TimeSpan.FromHours(-5))));
        Assert.Contains("Anything untouched is approved Sunday evening.", Rendered(evening));
    }

    [Fact]
    public void TheVoiceIsInTheOwnersHandOrAbsent()
    {
        Assert.Equal(
            ["Soup weather is here. One new dish, Thursday,", "and Friday is somebody else’s kitchen."],
            Texts(MealSheetSamples.Page(), PrintedRole.Voice));
        Assert.DoesNotContain(MealSheetSamples.Page(MealSheetSamples.Model(voice: null)).Printed, i => i.Role == PrintedRole.Voice);
    }

    [Fact]
    public void LeftoversAndNothingPlannedRows()
    {
        var dinners = MealSheetSamples.Dinners().ToList();
        dinners[5] = DinnerChoice.Leftovers(MealSheetSamples.Monday.AddDays(5), "Thursday's pan makes enough.");
        dinners[6] = DinnerChoice.NothingPlanned(MealSheetSamples.Monday.AddDays(6), "Company comes.");
        var page = MealSheetSamples.Page(MealSheetSamples.Model(dinners));
        var text = Rendered(page);

        Assert.Contains("Leftovers", text);
        Assert.Contains("Nothing planned", text);
        Assert.Contains("Company comes.", text);
        Assert.NotNull(page.Map.TryGet(MealSheetIds.DinnerYesSaturday));
        Assert.NotNull(page.Map.TryGet(MealSheetIds.DinnerReasonSunday));
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void RendersOnePageAtTheTabletSize()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(MealSheetDocument.Build(MealSheetSamples.Model())));

        Assert.Equal(1, document.NumberOfPages);
        Assert.Equal((445d, 594d), (Math.Round(document.GetPage(1).Width), Math.Round(document.GetPage(1).Height)));
    }

    [Fact]
    public void TheShadedFieldIsDrawnWhereTheTargetIs()
    {
        var page = MealSheetSamples.Page();
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var paths = PdfReadBack.Paths(document.GetPage(1));
        var muted = PdfReadBack.Rgb(PaperColorTokens.InkMuted);

        foreach (var day in Week)
        {
            var field = page.Map.ById(MealSheetIds.DinnerReason(day)).Bounds;
            var yes = page.Map.ById(MealSheetIds.DinnerYes(day)).Bounds;

            Assert.Contains(paths, p => p.Filled && PdfReadBack.Same(p.Bounds, field));
            Assert.Contains(paths, p => p.Stroked && PdfReadBack.Same(p.Bounds, field) && PdfReadBack.SameColour(p.Stroke, muted));
            Assert.Contains(paths, p => p.Stroked && PdfReadBack.Same(p.Bounds, yes) && PdfReadBack.SameColour(p.Stroke, muted));
        }
    }

    [Fact]
    public void LongDishNamesWrapInsideTheirColumn()
    {
        var dishes = MealSheetSamples.Dishes().ToDictionary(p => p.Key, p => p.Value);
        var lentil = MealSheetSamples.Id("lentil-soup");
        dishes[lentil] = new("Braised lentil soup with seeded crusty bread");
        var page = MealSheetSamples.Page(MealSheetSamples.Model(dishes: dishes));
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var first = document.GetPage(1);
        var zoneRight = page.Map.ById(MealSheetIds.DinnerReason(DayOfWeek.Monday)).Bounds.Left;
        var field = page.Map.ById(MealSheetIds.DinnerReason(DayOfWeek.Monday)).Bounds;
        var rowTop = page.Map.ById(MealSheetIds.DinnerYes(DayOfWeek.Monday)).Bounds.Top - 20f;

        var words = first.GetWords()
            .Where(w => PdfReadBack.Flip(first, w.BoundingBox) is var b && b.Top >= rowTop && b.Bottom <= rowTop + 58f && b.Left > 90f && b.Right < zoneRight && w.Text is not ("No," or "because"))
            .ToList();

        Assert.Contains(words, w => w.Text == "bread");
        Assert.All(words, w => Assert.True(PdfReadBack.Flip(first, w.BoundingBox).Right <= zoneRight - 64f + 0.5f, $"'{w.Text}' runs past the dish column"));
        Assert.DoesNotContain(words, w => PdfReadBack.Flip(first, w.BoundingBox).Overlaps(field.Inflate(8f)));
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void EveryRowWordStaysInsideItsColumn()
    {
        var dishes = MealSheetSamples.Dishes().ToDictionary(p => p.Key, p => p.Value);
        dishes[MealSheetSamples.Id("turkey-meatballs")] = new("Wholemeal spaghetti, turkey meatballs");
        var page = MealSheetSamples.Page(MealSheetSamples.Model(dishes: dishes));
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var first = document.GetPage(1);

        foreach (var item in page.Printed.Where(i => i.Name.EndsWith("-line", StringComparison.Ordinal)))
        {
            var drawn = first.GetWords()
                .Select(w => PdfReadBack.Flip(first, w.BoundingBox))
                .Where(b => b.CentreY > item.Bounds.Top && b.CentreY < item.Bounds.Bottom && b.Left >= item.Bounds.Left - 0.5f && b.Left < item.Bounds.Right)
                .ToList();

            Assert.NotEmpty(drawn);
            Assert.True(drawn.Max(b => b.Right) <= item.Bounds.Right + 0.5f, $"'{item.Text}' runs past its column ({drawn.Max(b => b.Right):0.0} > {item.Bounds.Right:0.0})");
        }
    }

    [Fact]
    public void AMarkersWordSitsInsideItsOutline()
    {
        var page = MealSheetSamples.Page();
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var first = document.GetPage(1);

        foreach (var day in new[] { "wednesday", "thursday", "friday" })
        {
            var left = page.Printed.Single(i => i.Name == $"{day}-marker-left").Bounds;
            var right = page.Printed.Single(i => i.Name == $"{day}-marker-right").Bounds;
            var word = page.Printed.Single(i => i.Name == $"{day}-marker");
            var drawn = first.GetWords().Select(w => PdfReadBack.Flip(first, w.BoundingBox))
                .Where(b => b.CentreY > left.Top && b.CentreY < left.Bottom && b.Left >= left.Left - 0.5f && b.Left < right.Left).ToList();

            Assert.NotEmpty(drawn);
            Assert.True(drawn.Min(b => b.Left) >= left.Left + 2f, $"{word.Text} starts outside its outline");
            Assert.True(drawn.Max(b => b.Right) <= right.Left - 2f, $"{word.Text} runs past its outline");
        }
    }

    [Fact]
    public void ADishThatCannotFitItsRowIsRefused()
    {
        var dishes = MealSheetSamples.Dishes().ToDictionary(p => p.Key, p => p.Value);
        dishes[MealSheetSamples.Id("lentil-soup")] = new(string.Join(' ', Enumerable.Repeat("extraordinarily elaborate", 12)));

        var error = Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(dishes: dishes)));
        Assert.Contains("Monday", error.Message);
    }

    [Fact]
    public void AWeekThatWasNotPlannedHasNoSheet()
    {
        var model = MealSheetSamples.Model() with { Week = MealWeek.NotPlanned(MealSheetSamples.Monday, MealStop.BoxTooSmall, "Fewer than ten cards.") };

        Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(model));
    }

    [Fact]
    public void ACookedDinnerWithNoDishIsRefused()
    {
        var dishes = MealSheetSamples.Dishes().Where(p => p.Key.Value != "miso-salmon").ToDictionary(p => p.Key, p => p.Value);

        var error = Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(MealSheetSamples.Model(dishes: dishes)));
        Assert.Contains("Thursday", error.Message);
    }

    [Fact]
    public void TheIdsAreFrozenLiterals()
    {
        Assert.Equal(
            ["dinner-mon-yes", "dinner-tue-yes", "dinner-wed-yes", "dinner-thu-yes", "dinner-fri-yes", "dinner-sat-yes", "dinner-sun-yes"],
            Week.Select(MealSheetIds.DinnerYes));
        Assert.Equal(
            ["dinner-mon-why", "dinner-tue-why", "dinner-wed-why", "dinner-thu-why", "dinner-fri-why", "dinner-sat-why", "dinner-sun-why"],
            Week.Select(MealSheetIds.DinnerReason));
    }
}
