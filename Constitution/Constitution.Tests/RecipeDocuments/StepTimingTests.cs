using System.Text.RegularExpressions;
using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;
using UglyToad.PdfPig.Content;

namespace Constitution.Tests.RecipeDocuments;

/// <summary>C-2.5.2: the steps on the clock, worked back from dinner (book p.8).</summary>
public class StepTimingTests
{
    private static readonly DateTime Dinner = RecipeSamples.Tuesday.AddHours(18).AddMinutes(15);
    private static readonly DateTime Morning = RecipeSamples.Tuesday.AddHours(7).AddMinutes(30);

    private static IReadOnlyList<InkPageLayout> Pages(RecipeDocumentModel? model = null) => RecipeDocument.Build(model ?? RecipeSamples.Timed());

    private static InkPageLayout Steps(RecipeDocumentModel? model = null) => Pages(model)[1];

    private static IEnumerable<string> Texts(InkPageLayout page, PrintedRole role) =>
        page.Printed.Where(i => i.Role == role && i.Text is not null).Select(i => i.Text!);

    private static string Face(DateTime at) => TimeFormats(at);

    private static string TimeFormats(DateTime at) => Constitution.Domain.Time.TimeFormats.Clock12(at)[..^3];

    private static float Baseline(Page page, string word, int skip = 0) =>
        (float)(page.Height - page.GetWords().Where(w => w.Text == word).Skip(skip).First().Letters[0].StartBaseLine.Y);

    private static RecipeClockStep Step(int minutes, bool morning = false) => new($"Do the thing for {minutes} minutes.", minutes, Morning: morning);

    // ---- the arithmetic

    [Fact]
    public void TimesWorkBackFromDinner()
    {
        var clock = RecipeSchedule.Plan(RecipeSamples.TimedSteps(), Dinner, Morning);

        Assert.Equal(["5:15", "5:20", "5:30", "5:35", "5:45", "6:05", "6:10", "6:15"], clock.Rows.Select(r => Face(r.At)));
        Assert.Null(clock.SqueezedTo);
        Assert.True(clock.Rows[^1].IsDinner);
        Assert.All(clock.Rows.Take(7), r => Assert.NotNull(r.Step));
    }

    [Fact]
    public void TheCookBlockEndsExactlyAtDinner()
    {
        var clock = RecipeSchedule.Plan(RecipeSamples.TimedSteps(), Dinner, Morning);
        var last = clock.Rows[^2];

        Assert.Equal(Dinner, last.At.AddMinutes(last.Step!.Minutes));
        Assert.Equal(Dinner, clock.Rows[^1].At);
        Assert.Equal(Dinner.AddMinutes(-60), clock.Rows[0].At);
    }

    [Fact]
    public void EachStepStartsWhenThePreviousOneIsDone()
    {
        var clock = RecipeSchedule.Plan(RecipeSamples.TimedSteps(), Dinner, Morning);

        for (var i = 1; i < clock.Rows.Count; i++)
        {
            Assert.Equal(clock.Rows[i].At, clock.Rows[i - 1].At.AddMinutes(clock.Rows[i - 1].Step!.Minutes));
        }
    }

    [Fact]
    public void StepsNeverStartBeforeNow()
    {
        var now = Dinner.AddMinutes(-35);
        var clock = RecipeSchedule.Plan(RecipeSamples.TimedSteps(), Dinner, Morning, now);

        Assert.All(clock.Rows, r => Assert.True(r.At >= now, $"{Face(r.At)} is before now ({Face(now)})."));
        Assert.Equal(now, clock.SqueezedTo);
        Assert.Equal(Dinner, clock.Rows[^1].At);
        Assert.Equal(clock.Rows.Select(r => r.At).Order(), clock.Rows.Select(r => r.At));
        Assert.Equal(now, clock.Rows[0].At);
    }

    [Fact]
    public void AnEarlyNowSqueezesNothing()
    {
        var early = RecipeSchedule.Plan(RecipeSamples.TimedSteps(), Dinner, Morning, Dinner.AddMinutes(-60));
        var plain = RecipeSchedule.Plan(RecipeSamples.TimedSteps(), Dinner, Morning, null);

        Assert.Null(early.SqueezedTo);
        Assert.Equal(plain.Rows.Select(r => r.At), early.Rows.Select(r => r.At));
        Assert.Null(RecipeSchedule.Plan(RecipeSamples.TimedSteps(), Dinner, Morning, Dinner.AddMinutes(-61)).SqueezedTo);
    }

    [Theory]
    [InlineData(2026, 11, 1)]
    [InlineData(2026, 3, 8)]
    [InlineData(2026, 6, 14)]
    public void ADaylightSavingEveningReadsTheSameOnTheClock(int year, int month, int day)
    {
        foreach (var kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Local })
        {
            var dinner = new DateTime(year, month, day, 18, 15, 0, kind);
            var clock = RecipeSchedule.Plan(RecipeSamples.TimedSteps(), dinner, new DateTime(year, month, day, 7, 30, 0, kind));

            Assert.Equal(["5:15", "5:20", "5:30", "5:35", "5:45", "6:05", "6:10", "6:15"], clock.Rows.Select(r => Face(r.At)));
        }
    }

    [Fact]
    public void ADaylightSavingMorningStillGoesForwardOnTheWallClock()
    {
        var day = new DateTime(2026, 11, 1);
        var dinner = day.AddHours(18).AddMinutes(15);
        var model = RecipeSamples.SlowCooker();
        var clock = RecipeSchedule.Plan(model.Steps!, dinner, day.AddHours(7).AddMinutes(30));

        Assert.Equal(["7:30", "7:40", "5:50", "6:05", "6:15"], clock.Rows.Select(r => Face(r.At)));
    }

    [Fact]
    public void SlowCookerMorningStepsComeFirst()
    {
        var model = RecipeSamples.SlowCooker();
        var clock = RecipeSchedule.Plan(model.Steps!, model.DinnerAt, model.StartAt);

        Assert.Equal(["7:30", "7:40", "5:50", "6:05", "6:15"], clock.Rows.Select(r => Face(r.At)));
        Assert.Equal(["Onion and garlic chopped; in with beans, tomatoes, spices.", "Lid on. Low.", "Stir. Taste for salt.", "Toppings out: cheese, yogurt, scallions."],
            clock.Rows.Where(r => !r.IsDinner).Select(r => r.Step!.Text));
    }

    [Fact]
    public void MorningStepsFollowOneAnotherForward()
    {
        var clock = RecipeSchedule.Plan([Step(10, true), Step(25, true), Step(5)], Dinner, Morning);

        Assert.Equal(Morning, clock.Rows[0].At);
        Assert.Equal(Morning.AddMinutes(10), clock.Rows[1].At);
        Assert.Equal(Dinner.AddMinutes(-5), clock.Rows[2].At);
    }

    [Fact]
    public void ANowAfterTheMorningStartMovesTheMorningForward()
    {
        var now = Morning.AddMinutes(20);
        var clock = RecipeSchedule.Plan([Step(10, true), Step(25, true), Step(5)], Dinner, Morning, now);

        Assert.Equal(now, clock.Rows[0].At);
        Assert.Equal(now.AddMinutes(10), clock.Rows[1].At);
        Assert.Equal(now, clock.SqueezedTo);
    }

    [Fact]
    public void TheTotalAndTheHandsOnTimeCountOnlyTheirOwnSteps()
    {
        Assert.Equal(60, RecipeSchedule.TotalMinutes(RecipeSamples.TimedSteps()));
        Assert.Equal(30, RecipeSchedule.HandsOnMinutes(RecipeSamples.TimedSteps()));
    }

    [Fact]
    public void ForAnyStepsTheClockIsOrderedAndLandsOnDinner()
    {
        var random = new Random(1317);
        for (var round = 0; round < 200; round++)
        {
            var steps = Enumerable.Range(0, random.Next(1, 14)).Select(_ => Step(random.Next(1, 75))).ToList();
            var now = random.Next(3) == 0 ? Dinner.AddMinutes(-random.Next(0, 300)) : (DateTime?)null;
            var clock = RecipeSchedule.Plan(steps, Dinner, Morning, now);

            Assert.Equal(steps.Count + 1, clock.Rows.Count);
            Assert.Equal(Dinner, clock.Rows[^1].At);
            Assert.Equal(clock.Rows.Select(r => r.At).Order(), clock.Rows.Select(r => r.At));
            Assert.All(clock.Rows, r => Assert.True(now is null || r.At >= now, $"round {round}"));
            if (clock.SqueezedTo is null)
            {
                Assert.Equal(Dinner, clock.Rows[^2].At.AddMinutes(steps[^1].Minutes));
                Assert.Equal(Dinner.AddMinutes(-steps.Sum(s => s.Minutes)), clock.Rows[0].At);
            }
        }
    }

    [Fact]
    public void AStepWithoutMinutesIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RecipeSchedule.Plan([Step(0)], Dinner, Morning));
        Assert.Throws<ArgumentException>(() => RecipeSchedule.Plan([], Dinner, Morning));
    }

    // ---- the page

    [Fact]
    public void TheStepsPageIsAsDrawn()
    {
        var page = Steps();

        Assert.Equal("Steps", page.Title);
        Assert.Contains("RECIPE · TIMED BACK FROM DINNER AT 6:15", Texts(page, PrintedRole.Label));
        Assert.Equal("Constitution · Recipe · Tue Nov 17 · Lee", page.Printed.Single(i => i.Name == "footer").Text);
        Assert.Equal("2 of 4", page.Printed.Single(i => i.Name == "page-number").Text);
        Assert.Contains("Clock times tonight, from dinner back. Late? Every step moves together.", Texts(page, PrintedRole.Small));
    }

    [Fact]
    public void EveryRowCarriesATimeInTwelveHourFaceAtTheTitleRungInCascadia()
    {
        var page = Steps();
        var times = Texts(page, PrintedRole.Time).ToList();

        Assert.Equal(["5:15", "5:20", "5:30", "5:35", "5:45", "6:05", "6:10", "6:15"], times);
        Assert.All(times, t => Assert.Matches(new Regex(@"^(1[0-2]|[1-9]):[0-5][0-9]$", RegexOptions.None, TimeSpan.FromSeconds(1)), t));

        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var letters = document.GetPage(1).Letters.Where(l => l.FontName.Contains("Cascadia", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(times.Sum(t => t.Length), letters.Count);
        Assert.All(letters, l => Assert.Contains("Cascadia", l.FontName, StringComparison.OrdinalIgnoreCase));
        Assert.All(letters, l => Assert.Equal(PaperTypeScale.Title, (float)l.PointSize, 0.1f));
    }

    [Fact]
    public void NoTimeIsWrittenOnA24HourClock()
    {
        var text = string.Join(" ", Pages().SelectMany(p => p.Printed).Select(i => i.Text ?? string.Empty));

        Assert.DoesNotMatch(new Regex(@"\b(1[3-9]|2[0-3]):[0-5][0-9]\b", RegexOptions.None, TimeSpan.FromSeconds(1)), text);
        Assert.Contains("Dinner 6:15 pm · start 5:15 pm", text);
    }

    [Fact]
    public void TheRowsSitOnTheBooksBaselines()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Steps()]));
        var page = document.GetPage(1);

        Assert.Equal(112f, Baseline(page, "Clock"), 1f);
        Assert.Equal(142f, Baseline(page, "5:15"), 1.5f);
        Assert.Equal(138f, Baseline(page, "Oven"), 1.5f);
        Assert.Equal(154f, Baseline(page, "Kids’"), 1.5f);
        Assert.Equal(189f, Baseline(page, "5:20"), 1.5f);
        Assert.Equal(201f, Baseline(page, "pepper."), 1.5f);
        Assert.Equal(239f, Baseline(page, "5:30"), 1.5f);
        Assert.Equal(286f, Baseline(page, "5:35"), 1.5f);
        Assert.Equal(336f, Baseline(page, "5:45"), 1.5f);
        Assert.Equal(399f, Baseline(page, "6:05"), 1.5f);
        Assert.Equal(462f, Baseline(page, "6:10"), 1.5f);
        Assert.Equal(508f, Baseline(page, "6:15", 1), 1.5f);
        Assert.Equal(504f, Baseline(page, "Dinner."), 1.5f);
    }

    [Fact]
    public void ARuleSeparatesEachRowAndTheTimeStartsAtTheMargin()
    {
        var page = Steps();
        var rules = page.Printed.Where(i => i.Role == PrintedRole.Rule && i.Name.StartsWith("step-rule-", StringComparison.Ordinal)).Select(i => i.Bounds.Top).ToList();

        Assert.Equal([120f, 167f, 217f, 264f, 314f, 377f, 440f, 486f], rules);
        Assert.All(page.Printed.Where(i => i.Role == PrintedRole.Time), i => Assert.Equal(36f, i.Bounds.Left));
        Assert.All(page.Printed.Where(i => i.Name.StartsWith("step-", StringComparison.Ordinal) && i.Role == PrintedRole.Body), i => Assert.Equal(104f, i.Bounds.Left));
    }

    [Fact]
    public void AsidesCarryTheWhyAndTheHandOffToTheKidsPage()
    {
        var small = Texts(Steps(), PrintedRole.Small).ToList();

        Assert.Contains("Kids’ noodle water on now.", small);
        Assert.Contains("In first: they need longer.", small);
        Assert.Contains("Kids’ noodles: page 3.", small);
        Assert.Contains("Not there? Five more minutes.", small);
    }

    [Fact]
    public void TheNotesMarginSitsWhereTheBookDrawsItOnThisPage()
    {
        var page = Steps();
        var notes = Assert.Single(page.Map.Targets);

        Assert.Equal(RecipeIds.Notes, notes.Id);
        Assert.Equal(124f, notes.Bounds.Top);
        Assert.Empty(page.Violations());
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        Assert.Equal(140f, Baseline(document.GetPage(1), "NOTES"), 1.5f);
    }

    [Fact]
    public void NoWordCrossesIntoTheNotesMargin()
    {
        foreach (var model in LongStepModels())
        {
            foreach (var page in Pages(model).Skip(1))
            {
                using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
                var sheet = document.GetPage(1);

                // The lead line runs full width above the notes column, which starts below it; the rows are what must keep out.
                var letters = sheet.Letters.Where(l => sheet.Height - l.GlyphRectangle.Top >= 122 && l.GlyphRectangle.Top > 40).ToList();

                Assert.NotEmpty(letters);
                Assert.DoesNotContain(letters, l => l.GlyphRectangle.Left < 314 && l.GlyphRectangle.Right > 308);
            }
        }
    }

    [Fact]
    public void LongStepsAndAsidesWrapWithinTheirColumnByRealGlyphWidths()
    {
        using var body = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);
        using var small = new SKFont(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary);
        using var time = new SKFont(EmbeddedTypefaces.Mono, PaperTypeScale.Title);
        var measured = 0;

        foreach (var model in LongStepModels())
        {
            foreach (var page in Pages(model).Skip(1))
            {
                foreach (var item in page.Printed.Where(i => i.Name.StartsWith("step-", StringComparison.Ordinal) && i.Text is not null))
                {
                    var font = item.Role == PrintedRole.Time ? time : item.Role == PrintedRole.Small ? small : body;
                    Assert.True(font.MeasureText(item.Text!) <= item.Bounds.Width, $"'{item.Text}' is {font.MeasureText(item.Text!):0} points in a {item.Bounds.Width:0} point column.");
                    measured++;
                }
            }
        }

        Assert.True(measured >= 60, $"Only {measured} lines were measured.");
    }

    [Fact]
    public void EveryLongPageIsFitToPrintAndKeepsClearOfTheFooter()
    {
        foreach (var model in LongStepModels())
        {
            foreach (var page in Pages(model))
            {
                Assert.Empty(page.Violations());
                var footer = page.Printed.Single(i => i.Name == "footer").Bounds;
                foreach (var item in page.Printed.Where(i => i.Name.StartsWith("step-", StringComparison.Ordinal) && i.Role != PrintedRole.Rule))
                {
                    Assert.True(item.Bounds.Bottom < footer.Top, $"'{item.Text}' reaches {item.Bounds.Bottom:0}, into the footer at {footer.Top:0}.");
                }
            }
        }
    }

    [Fact]
    public void OnlyTheHeaderRuleIsColouredAndEverythingElseIsMuted()
    {
        foreach (var page in Pages().Skip(1))
        {
            using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
            var neutral = new[]
            {
                PaperColorTokens.Paper, PaperColorTokens.PaperBright, PaperColorTokens.PaperDeep, PaperColorTokens.PaperShade,
                PaperColorTokens.Hairline, PaperColorTokens.Ink, PaperColorTokens.InkSoft, PaperColorTokens.InkMuted,
            }.Select(PdfReadBack.Rgb).ToList();
            var header = PdfReadBack.Rgb(AppIdentityTokens.ConstitutionDocument);

            var coloured = PdfReadBack.PaintedColours(document.GetPage(1))
                .Where(c => !neutral.Any(n => PdfReadBack.SameColour((c.R, c.G, c.B), n)) && !(c.R > 0.99 && c.G > 0.99 && c.B > 0.99))
                .ToList();

            Assert.Single(coloured);
            Assert.True(PdfReadBack.SameColour((coloured[0].R, coloured[0].G, coloured[0].B), header));
        }
    }

    [Fact]
    public void StepRulesPrintInTheMutedInk()
    {
        var page = Steps();
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var muted = PdfReadBack.Rgb(PaperColorTokens.InkMuted);

        foreach (var rule in page.Printed.Where(i => i.Name.StartsWith("step-rule-", StringComparison.Ordinal)))
        {
            var stroked = PdfReadBack.Paths(document.GetPage(1)).Where(p => p.Stroked && PdfReadBack.Same(p.Bounds, rule.Bounds)).ToList();
            Assert.NotEmpty(stroked);
            Assert.All(stroked, p => Assert.True(PdfReadBack.SameColour(p.Stroke, muted)));
        }
    }

    [Fact]
    public void TheTimeBodyAndAsideAreThreeDifferentInks()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Steps()]));
        var words = document.GetPage(1).GetWords().ToList();
        var time = words.First(w => w.Text == "5:15").Letters[0];
        var aside = words.First(w => w.Text == "Kids’").Letters[0];
        var body = words.First(w => w.Text == "Oven").Letters[0];

        Assert.Contains("Cascadia", time.FontName, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual((float)body.PointSize, (float)aside.PointSize);
        Assert.NotEqual((float)body.PointSize, (float)time.PointSize);
        Assert.False(PdfReadBack.SameColour(Rgb(aside), Rgb(body)));
        static (double, double, double) Rgb(Letter l) => l.Color.ToRGBValues();
    }

    [Fact]
    public void OnlyRegisteredTypeRungsArePrinted()
    {
        var allowed = new[] { PaperTypeScale.Title, PaperTypeScale.Body, PaperTypeScale.Secondary, PaperTypeScale.Label, PaperTypeScale.Mono };
        foreach (var page in Pages())
        {
            using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
            Assert.All(document.GetPage(1).Letters.Select(l => (float)l.PointSize).Distinct(), size => Assert.Contains(allowed, rung => Math.Abs(rung - size) < 0.1f));
        }
    }

    [Fact]
    public void TheTimeRoleInkBoxIsTheFacesOwn()
    {
        using var time = new SKFont(EmbeddedTypefaces.Mono, PaperTypeScale.Title);
        var item = Steps().Printed.First(i => i.Role == PrintedRole.Time);

        Assert.Equal(time.Metrics.Descent - time.Metrics.Ascent, item.Bounds.Height, 0.3f);
        Assert.Equal(time.Metrics.Descent, item.Bounds.Bottom - 142f, 0.1f);
    }

    // ---- the slow cooker

    [Fact]
    public void TheSlowCookerPageIsAsDrawn()
    {
        var pages = RecipeDocument.Build(RecipeSamples.SlowCooker());
        var page = pages[1];

        Assert.Equal(3, pages.Count + 1);
        Assert.Equal("Black bean chili", page.Title);
        Assert.Contains("RECIPE · SLOW COOKER · ARRIVES BEFORE THE MORNING BLOCK", Texts(page, PrintedRole.Label));
        Assert.Contains("Started in the morning, ready at dinner. Morning steps first.", Texts(page, PrintedRole.Small));
        Assert.Equal(["7:30", "7:40", "5:50", "6:05", "6:15"], Texts(page, PrintedRole.Time));
        Assert.Equal("Constitution · Recipe · Wed Nov 18 · Sam", page.Printed.Single(i => i.Name == "footer").Text);
        Assert.Equal("2 of 3", page.Printed.Single(i => i.Name == "page-number").Text);
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void TheMorningStepNamesTheCalendarEvent()
    {
        var page = RecipeDocument.Build(RecipeSamples.SlowCooker())[1];

        Assert.Contains(page.Printed, i => i.Role == PrintedRole.Small && i.Text == "On the calendar: “Start the slow cooker.”");
        var timeAt = page.Printed.Single(i => i.Role == PrintedRole.Time && i.Text == "7:40").Bounds.Top;
        var asideAt = page.Printed.Single(i => i.Text == "On the calendar: “Start the slow cooker.”").Bounds.Top;
        Assert.InRange(asideAt - timeAt, 0f, 40f);
    }

    [Fact]
    public void TheSlowCookerPageOneStartsInTheMorning()
    {
        var first = RecipeDocument.Build(RecipeSamples.SlowCooker())[0];

        Assert.Contains("Dinner 6:15 pm · start 7:30 am", string.Join(" ", Texts(first, PrintedRole.Small)));
    }

    // ---- numbering and flow

    [Fact]
    public void PagesAreNumberedAcrossTheWholeRecipe()
    {
        var pages = Pages();

        Assert.Equal(["1 of 4", "2 of 4", "3 of 4"], pages.Select(p => p.Printed.Single(i => i.Name == "page-number").Text));
        Assert.Contains("The children’s dinner is on page 3.", Texts(pages[0], PrintedRole.Small));
    }

    [Fact]
    public void WithoutAKidsMealTheRecipeIsShorterAndNamesNoHandOff()
    {
        var pages = RecipeDocument.Build(RecipeSamples.Timed(steps: RecipeSamples.TimedSteps().Select(s => s with { KidsHandOff = false }).ToList(), noKids: true));

        Assert.Equal(2, pages.Count);
        Assert.Equal("2 of 3", pages[1].Printed.Single(i => i.Name == "page-number").Text);
        Assert.DoesNotContain(pages.SelectMany(p => p.Printed), i => i.Text is not null && i.Text.Contains("children’s", StringComparison.Ordinal));
        Assert.DoesNotContain(pages.SelectMany(p => p.Printed), i => i.Text is not null && i.Text.Contains("Kids’ noodles", StringComparison.Ordinal));
    }

    [Fact]
    public void AHandOffWithNowhereToGoIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => RecipeDocument.Build(RecipeSamples.Timed(noKids: true)));
        Assert.Contains("children", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StepsThatDoNotFitOnePageFlowToAContinuationPage()
    {
        var model = RecipeSamples.Timed(steps: ManySteps(18), noKids: true);
        var pages = RecipeDocument.Build(model);
        var stepPages = pages.Skip(1).ToList();

        Assert.True(stepPages.Count >= 2, $"{stepPages.Count} step pages");
        Assert.Equal(Enumerable.Range(2, stepPages.Count).Select(n => $"{n} of {pages.Count + 1}"), stepPages.Select(p => p.Printed.Single(i => i.Name == "page-number").Text));
        Assert.Contains("RECIPE · TIMED BACK FROM DINNER AT 6:15 · CONTINUED", Texts(stepPages[1], PrintedRole.Label));
        Assert.All(stepPages, p => Assert.Equal("Steps", p.Title));

        var times = stepPages.SelectMany(p => Texts(p, PrintedRole.Time)).ToList();
        Assert.Equal(19, times.Count);
        Assert.Equal("6:15", times[^1]);
        var texts = stepPages.SelectMany(p => p.Printed).Where(i => i.Name.StartsWith("step-", StringComparison.Ordinal) && i.Role == PrintedRole.Body).Select(i => i.Text!).ToList();
        Assert.Equal(18, texts.Count(t => t.StartsWith("Step number", StringComparison.Ordinal)));
    }

    [Fact]
    public void ADinnerRowIsNeverLeftAloneOnAPage()
    {
        for (var count = 8; count <= 30; count++)
        {
            var pages = RecipeDocument.Build(RecipeSamples.Timed(steps: ManySteps(count), noKids: true));
            var last = pages[^1];

            Assert.True(last.Printed.Count(i => i.Role == PrintedRole.Time) >= 2, $"{count} steps leave dinner alone on the last page.");
        }
    }

    [Fact]
    public void EveryPageOfAFlowedRecipeSaysTheSameTotal()
    {
        var pages = RecipeDocument.Build(RecipeSamples.Timed(steps: ManySteps(25, handOff: true)));
        var total = pages.Count + 1;

        Assert.All(pages.Select((p, i) => (p, i)), x => Assert.Equal($"{x.i + 1} of {total}", x.p.Printed.Single(i => i.Name == "page-number").Text));
        var kids = pages.First(p => p.Title == "Buttered noodles");
        Assert.Equal(pages.Count, pages.ToList().IndexOf(kids) + 1);
        Assert.Contains($"The children’s dinner is on page {pages.Count}.", Texts(pages[0], PrintedRole.Small));
        Assert.Contains($"Kids’ noodles: page {pages.Count}.", pages.SelectMany(p => Texts(p, PrintedRole.Small)));
    }

    [Fact]
    public void TheStepsRenderAsOnePdfPagePerLayoutAtTheTabletSize()
    {
        var layouts = Pages(RecipeSamples.Timed(steps: ManySteps(18)));
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(layouts));

        Assert.Equal(layouts.Count, document.NumberOfPages);
        Assert.Equal((445d, 594d), (Math.Round(document.GetPage(2).Width), Math.Round(document.GetPage(2).Height)));
    }

    [Fact]
    public void WhenNowSqueezesTheClockThePageSaysSo()
    {
        var page = Steps(RecipeSamples.Timed(notBefore: Dinner.AddMinutes(-35)));

        Assert.Contains("Tight tonight: the first steps start together at 5:40.", Texts(page, PrintedRole.Small));
        Assert.DoesNotContain("Clock times tonight, from dinner back. Late? Every step moves together.", Texts(page, PrintedRole.Small));
        Assert.Equal("5:40", Texts(page, PrintedRole.Time).First());
    }

    [Fact]
    public void PageOneStartsWhereTheStepsDo()
    {
        var first = Pages(RecipeSamples.Timed(notBefore: Dinner.AddMinutes(-35)))[0];

        Assert.Contains("Dinner 6:15 pm · start 5:40 pm", string.Join(" ", Texts(first, PrintedRole.Small)));
    }

    [Fact]
    public void TheStepsAreChecked()
    {
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(steps: [])));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(steps: [new("  ", 5)])));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed(steps: [new("Stir.", 0)])));
        Assert.Throws<ArgumentException>(() => Pages(RecipeSamples.Timed() with { TrailingPages = -1 }));
    }

    // ---- the long samples

    private static List<RecipeClockStep> ManySteps(int count, bool handOff = false)
    {
        var steps = Enumerable.Range(1, count).Select(n => new RecipeClockStep(
            $"Step number {n}: stir the pot slowly, then wait until the sauce coats the back of a spoon.", 3, Aside: n % 3 == 0 ? $"Watch the heat at step {n}." : null)).ToList();
        steps[0] = steps[0] with { KidsHandOff = handOff };
        return steps;
    }

    private static IEnumerable<RecipeDocumentModel> LongStepModels()
    {
        string[][] sets =
        [
            [
                "Toss the boneless skinless chicken thighs, trimmed of any visible fat, with the marinade until evenly coated.",
                "MMMM WWWW mixed capitals in an unusually wide invented step that goes on for a good while longer",
                "Wondra flour, Worcestershire sauce, Wagyu beef and Wasabi mix, folded through quickly",
            ],
            [
                "Bring a very large pot of pasta water to a rolling boil and salt it until it tastes of the sea",
                "Rest.",
                "Spoon the fire-roasted diced tomatoes with their juices over the toasted pumpkin seeds, lightly salted.",
            ],
        ];

        foreach (var set in sets)
        {
            var steps = set.Select((text, i) => new RecipeClockStep(text, 7 + i, Aside: i == 0 ? "Why: the marinade needs the time, and the chicken needs the heat to be even all through." : null, KidsHandOff: i == 1)).ToList();
            yield return RecipeSamples.Timed(steps: steps);
            yield return RecipeSamples.Timed(steps: steps.Concat(ManySteps(12)).ToList());
        }
    }
}
