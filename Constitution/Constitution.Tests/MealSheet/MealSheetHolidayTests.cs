using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using Storyworthtelling.Paper;

namespace Constitution.Tests.MealSheet;

/// <summary>C-2.4.4: the holiday ask-ahead. Only the week before a holiday the household hosts or visits; every date here is invented.</summary>
public class MealSheetHolidayTests
{
    private static readonly DateOnly Week = MealSheetSamples.Sunday;

    private static HolidayEvent Feast(DateOnly date, HolidayRole role = HolidayRole.Visiting, string name = "Thanksgiving") =>
        new(name, date, role, "the Okafors’", new TimeOnly(15, 0));

    private sealed class FakeCalendar(params HolidayEvent[] events) : IHolidayCalendar
    {
        public List<(DateOnly First, DateOnly Last)> Asked { get; } = [];

        public HolidayEvents Between(DateOnly first, DateOnly last)
        {
            Asked.Add((first, last));
            return new HolidayEvents(true, events.Where(e => e.Date >= first && e.Date <= last).ToList());
        }
    }

    private static HolidayAsk? Decide(params HolidayEvent[] events) => HolidayAskAhead.Decide(new FakeCalendar(events), Week);

    private static HolidayAsk Ask() => Decide(Feast(Week.AddDays(11)))!;

    private static string Rendered(InkPageLayout page)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        return WorkoutCoverTests.TextOf(document.GetPage(1));
    }

    [Fact]
    public void AskAheadOnlyTheWeekBefore()
    {
        Assert.NotNull(Decide(Feast(Week.AddDays(7))));
        Assert.NotNull(Decide(Feast(Week.AddDays(13))));
        Assert.NotNull(Decide(Feast(Week.AddDays(11), HolidayRole.Hosting)));
        Assert.Null(Decide(Feast(Week.AddDays(6))));
        Assert.Null(Decide(Feast(Week)));
        Assert.Null(Decide(Feast(Week.AddDays(14))));
        Assert.Null(Decide(Feast(Week.AddDays(-3))));
    }

    [Fact]
    public void TheCalendarIsAskedForExactlyTheNextWeek()
    {
        var calendar = new FakeCalendar();

        Assert.Null(HolidayAskAhead.Decide(calendar, Week));
        Assert.Equal([(Week.AddDays(7), Week.AddDays(13))], calendar.Asked);
    }

    [Fact]
    public void AHolidayAtHomeAsksNothing() =>
        Assert.Null(Decide(Feast(Week.AddDays(11), HolidayRole.StayingHome)));

    [Fact]
    public void ACalendarThatCannotBeAskedAsksNothing()
    {
        Assert.False(new UnavailableHolidayCalendar().Between(Week, Week.AddDays(6)).IsAvailable);
        Assert.Null(HolidayAskAhead.Decide(new UnavailableHolidayCalendar(), Week));
    }

    [Fact]
    public void TheEarliestHolidayIsAsked()
    {
        var ask = Decide(Feast(Week.AddDays(12), name: "Later feast"), Feast(Week.AddDays(9), name: "Earlier feast"));

        Assert.Equal("Earlier feast", ask!.Title);
    }

    [Fact]
    public void TheLineSaysWhenWhereAndWhen()
    {
        Assert.Equal("Thursday, Nov 26 · at the Okafors’ · dinner 3:00 pm", Ask().WhenLine);
        Assert.Equal(
            "Thursday, Nov 26 · hosting here · dinner 3:00 pm",
            Decide(Feast(Week.AddDays(11), HolidayRole.Hosting))!.WhenLine);
    }

    [Fact]
    public void ADinnerWithNoTimeLeavesTheTimeOut() =>
        Assert.Equal(
            "Thursday, Nov 26 · at the Okafors’",
            Decide(new HolidayEvent("Thanksgiving", Week.AddDays(11), HolidayRole.Visiting, "the Okafors’"))!.WhenLine);

    [Fact]
    public void TheSheetCarriesTheAskPageOnlyWhenThereIsAnAsk()
    {
        var without = MealSheetGrocerySamples.Pages();
        var with = MealSheetGrocerySamples.Pages(MealSheetGrocerySamples.Model(ask: Ask()));

        Assert.Equal(4, without.Count);
        Assert.Equal(5, with.Count);
        Assert.Equal("Thanksgiving", with[4].Title);
    }

    [Fact]
    public void TheAskPageIsAsDrawn()
    {
        var page = MealSheetGrocerySamples.Pages(MealSheetGrocerySamples.Model(ask: Ask()))[4];
        var text = Rendered(page);

        Assert.Contains("WEEKLY MEAL SHEET · 5 OF 5 · ONLY THE WEEK BEFORE A HOLIDAY", text);
        Assert.Contains("Thursday, Nov 26 · at the Okafors’ · dinner 3:00 pm", text);
        Assert.Contains("What are we bringing?", text);
        Assert.Contains("Constitution · Meals · week of Nov 15 · both tablets", text);
        Assert.Equal("5 of 5", page.Printed.Single(i => i.Name == "page-number").Text);
        Assert.Empty(page.Map.Targets);
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void TheAskPageNamesTheDinnerItLeavesOff()
    {
        var text = Rendered(MealSheetGrocerySamples.Pages(MealSheetGrocerySamples.Model(ask: Ask()))[4]);

        Assert.Contains("Thursday’s dinner off", text);
    }

    [Fact]
    public void TheAskPageHasOnlyTheHeaderRuleInColour()
    {
        var page = MealSheetGrocerySamples.Pages(MealSheetGrocerySamples.Model(ask: Ask()))[4];
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
        Assert.All(coloured, c => Assert.True(PdfReadBack.SameColour((c.R, c.G, c.B), header)));
    }

    [Fact]
    public void ALongHolidayNameAndPlaceWrapInsideTheMeasure()
    {
        var ask = Decide(new HolidayEvent("The Juniper Street neighbourhood midwinter lantern supper", Week.AddDays(10), HolidayRole.Visiting, "the Robin and Lee Okafor family’s lakeside cabin", new TimeOnly(17, 30)))!;
        var page = MealSheetGrocerySamples.Pages(MealSheetGrocerySamples.Model(ask: ask))[4];
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var first = document.GetPage(1);

        foreach (var item in page.Printed.Where(i => i.Name.StartsWith("ask-", StringComparison.Ordinal) && i.Text is not null))
        {
            var drawn = first.GetWords().Select(w => PdfReadBack.Flip(first, w.BoundingBox))
                .Where(b => b.CentreY > item.Bounds.Top && b.CentreY < item.Bounds.Bottom && b.Left >= item.Bounds.Left - 0.5f).ToList();

            Assert.NotEmpty(drawn);
            Assert.True(drawn.Max(b => b.Right) <= item.Bounds.Right + 0.5f, $"'{item.Text}' runs past the measure");
        }

        Assert.Empty(page.Violations());
    }

    [Fact]
    public void TheAskPageTakesNoPartInTheInkRules()
    {
        var model = MealSheetGrocerySamples.Model(ask: Ask());
        var document = ExpectedDocument.ForMealSheet(model, "Sam");

        Assert.DoesNotContain(document.Pages, page => page.Number == 5);
    }

    [Fact]
    public void ThePageNumbersCountTheAskPage()
    {
        var pages = MealSheetGrocerySamples.Pages(MealSheetGrocerySamples.Model(ask: Ask()));

        Assert.Equal(
            ["1 of 5", "2 of 5", "3 of 5", "4 of 5", "5 of 5"],
            pages.Select(p => p.Printed.Single(i => i.Name == "page-number").Text));
    }

    [Fact]
    public void ASheetTooShortForTheAskPageIsRefused()
    {
        var model = MealSheetGrocerySamples.Model(ask: Ask(), pages: 4);

        Assert.Throws<ArgumentException>(() => MealSheetDocument.Build(model));
    }
}
