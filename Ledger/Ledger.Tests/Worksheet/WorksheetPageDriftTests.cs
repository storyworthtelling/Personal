using System.Globalization;
using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using Storyworthtelling.Paper;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Phase 4, "Worksheet pages" (mockup 13(a), A4 at 1:1 in points): the cosmetic drift between what the
/// worksheet painter drew and what the mockup draws. Every assertion reads the PDF back with PdfPig —
/// text positions, font names and sizes, stroked rules — so it holds the page to the drawing rather than
/// to the constants that were meant to produce it.
///
/// <para>The mockup's own CSS is the source for the numbers: <c>.ws-row</c> is a grid of
/// <c>1fr 110px 150px</c> with a 12px gap (so the last-month column's right edge is the box column's left
/// less the gap), <c>.ws-calc</c> is 270px wide, <c>.ws-head</c> and <c>.ws-sub</c> carry a 1.5px ink rule
/// under them, <c>.ws-row</c> a 1px hairline, <c>.ws-calc .r.tot</c> a 1px ink rule above "Left to
/// assign", <c>.rp-title</c> is 33px, <c>.ws-z .ws-nbox</c> is 24px, and the footer date is mono and
/// right-aligned.</para>
/// </summary>
public class WorksheetPageDriftTests
{
    private const float Tolerance = 1.2f;

    private static Domain.Worksheet.WorksheetPlan MockupPlan(string yearMonth = "2026-10", DateOnly? printedOn = null)
    {
        var categories = new[] { "Groceries", "Dining out", "Fuel", "Household", "Kids", "Utilities", "Subscriptions", "Medical", "Clothing", "Gifts" }
            .Select((name, i) => new WorksheetCategoryLine(Guid.NewGuid(), name, i == 0 ? 548m : 100m + i))
            .ToList();
        var setAside = new List<WorksheetSetAsideLine>
        {
            new(SetAsideLineKind.ExtraDebtPayment, WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", "to Meridian Card", 350m),
            new(SetAsideLineKind.Savings, WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Emergency fund", 225m),
            new(SetAsideLineKind.Savings, WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Summer trip", 135m),
        };
        return new Domain.Worksheet.WorksheetPlan(
            yearMonth, printedOn ?? new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194, categories, setAside);
    }

    private static PdfDocument Open(Domain.Worksheet.WorksheetPlan plan) =>
        PdfDocument.Open(new MemoryStream(WorksheetPainter.Paint(plan)));

    private static Word Single(Page page, string text) => page.GetWords().Single(w => w.Text == text);

    // ---- the real previous month ----

    [Fact]
    public void TheIntroAndTheCalcRowNameThePreviousMonth_NotLastMonth()
    {
        using var document = Open(MockupPlan());
        var text = document.GetPage(1).Text;

        Assert.Contains("keeps September's figure", text, StringComparison.Ordinal);
        Assert.Contains("September's spending", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Last month's spending", text, StringComparison.Ordinal);
        Assert.DoesNotContain("last month's figure", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNameIsDerivedFromTheMonth_JanuaryLooksBackToDecember()
    {
        using var document = Open(MockupPlan("2027-01"));
        var text = document.GetPage(1).Text;

        Assert.Contains("December's figure", text, StringComparison.Ordinal);
        Assert.Contains("December's spending", text, StringComparison.Ordinal);
        Assert.Contains("DECEMBER", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheColumnHeaderIsThePreviousMonthsName_AsInTheMockup()
    {
        using var document = Open(MockupPlan());
        var text = document.GetPage(1).Text;

        Assert.Contains("SEPTEMBER", text, StringComparison.Ordinal);
        Assert.Contains("OCTOBER LIMIT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LAST MONTH", text, StringComparison.Ordinal);
    }

    // ---- the last-month column sits where the grid puts it ----

    [Fact]
    public void LastMonthColumnRightEdge_IsTheBoxColumnLeftLessTheGap()
    {
        var boxLeft = WorksheetRegions.MarginLeft + WorksheetRegions.BodyWidth - WorksheetRegions.BoxColumnWidth;
        Assert.Equal(boxLeft - WorksheetRegions.ColumnGap, WorksheetRegions.LastMonthColumnRight, 0.01f);
        Assert.Equal(380.2f, WorksheetRegions.LastMonthColumnRight, 0.05f);
    }

    [Fact]
    public void TheLastMonthFiguresAndTheirHeader_AreRightAlignedAtTheMockupsX()
    {
        using var document = Open(MockupPlan());
        var page = document.GetPage(1);

        var figure = Single(page, "$548");
        Assert.Equal(WorksheetRegions.LastMonthColumnRight, (float)figure.BoundingBox.Right, Tolerance);

        var header = Single(page, "SEPTEMBER");
        Assert.Equal(WorksheetRegions.LastMonthColumnRight, (float)header.BoundingBox.Right, Tolerance);
    }

    // ---- the calc column, the title, the name box ----

    [Fact]
    public void TheCalcColumnIs270Wide_SoItsFiguresEndAtLeftPlus270()
    {
        using var document = Open(MockupPlan());
        var income = Single(document.GetPage(1), "$9,350");

        Assert.Equal(WorksheetRegions.MarginLeft + 270f, (float)income.BoundingBox.Right, Tolerance);
    }

    [Fact]
    public void TheTitleIsSetAtTheDisplayRung()
    {
        using var document = Open(MockupPlan());
        var title = document.GetPage(1).Letters.First(l => l.Value == "O" && l.PointSize > 20);

        Assert.Equal(PaperTypeScale.Display, (float)title.PointSize, 0.3f);
    }

    [Fact]
    public void TheWriteInNameBoxIs24High()
    {
        var plan = MockupPlan();
        var names = WorksheetPainter.BoxesFor(plan).Where(b => b.Name.StartsWith("new-name:", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, names.Count);
        Assert.All(names, b => Assert.Equal(24f, b.Height));
        Assert.Equal(24f, WorksheetRegions.NameBoxHeight);
    }

    [Fact]
    public void TheHeaderStillClearsTheTitleAtDisplaySize()
    {
        using var document = Open(MockupPlan());
        var page = document.GetPage(1);
        var title = page.Letters.Where(l => l.PointSize > 20).ToList();
        var titleBottom = title.Max(l => PdfProbe.Box(page, l.GlyphRectangle).Bottom);
        var identityRule = PdfProbe.HorizontalRules(page).First(r => Math.Abs(r.Width - 1.5) < 0.01 && r.Length > 400);

        Assert.True(titleBottom < identityRule.Top, $"title bottom {titleBottom:0.#} must sit above the header rule at {identityRule.Top:0.#}");
    }

    // ---- bold intro figure ----

    [Fact]
    public void TheTargetFigureInTheIntroIsBold_AndTheWordsAroundItAreNot()
    {
        using var document = Open(MockupPlan());
        var page = document.GetPage(1);

        var figure = page.GetWords().First(w => w.Text.StartsWith("$3,100", StringComparison.Ordinal));
        var around = page.GetWords().First(w => w.Text == "come");

        Assert.Contains("Semibold", figure.Letters[1].FontName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Semibold", around.Letters[0].FontName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheIntroWrapsInsideTheBody_NeverPastTheRightMargin()
    {
        using var document = Open(MockupPlan());
        var page = document.GetPage(1);
        var intro = page.GetWords().Where(w => PdfProbe.Box(page, w.BoundingBox).Top < WorksheetRegions.IncomeBlockTop).ToList();
        var right = WorksheetRegions.MarginLeft + WorksheetRegions.BodyWidth;

        Assert.Contains(intro, w => w.Text == "reads");
        Assert.All(intro, w => Assert.True(w.BoundingBox.Right <= right + 0.5, $"'{w.Text}' ends at {w.BoundingBox.Right:0.#}, past {right:0.#}"));
    }

    // ---- the footer ----

    [Fact]
    public void TheFooterDateIsMono_AndRightAlignedToTheBody()
    {
        using var document = Open(MockupPlan());
        var page = document.GetPage(1);
        var words = page.GetWords().Where(w => w.Text is "printed" or "Sep" or "30").ToList();
        var lastWord = words.Single(w => w.Text == "30");

        Assert.Equal(3, words.Count);
        Assert.Equal(WorksheetRegions.MarginLeft + WorksheetRegions.BodyWidth, (float)lastWord.BoundingBox.Right, Tolerance);
        Assert.All(words, w => Assert.Contains("Cascadia", w.Letters[0].FontName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ThePrintedDateIgnoresTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            string[] months = ["jan.", "fév.", "mar.", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc.", ""];
            odd.DateTimeFormat.AbbreviatedMonthNames = months;
            odd.DateTimeFormat.AbbreviatedMonthGenitiveNames = months; // "MMM d" reads the genitive table
            CultureInfo.CurrentCulture = odd;

            using var document = Open(MockupPlan());
            var text = document.GetPage(1).Text;

            Assert.Contains("printed Sep 30", text.Replace("  ", " "), StringComparison.Ordinal);
            Assert.DoesNotContain("sept.", text, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- the rules ----

    [Fact]
    public void HeadersAndSectionsCarryAnInkRuleOfOnePointFive()
    {
        using var document = Open(MockupPlan());
        var page = document.GetPage(1);
        var inkRules = PdfProbe.HorizontalRules(page)
            .Where(r => Math.Abs(r.Width - 1.5) < 0.01 && PdfProbe.IsInk(r) && r.Length > WorksheetRegions.BodyWidth - 2)
            .ToList();

        // CATEGORY header, SET ASIDE, ADD A CATEGORY.
        Assert.Equal(3, inkRules.Count);
        var layout = WorksheetRegions.Layout(MockupPlan().CategoryIdsInPrintedOrder, MockupPlan().SetAsideKeysInPrintedOrder);
        Assert.Contains(inkRules, r => Math.Abs(r.Y - (WorksheetRegions.CategoryHeaderTop + WorksheetRegions.SectionHeaderHeight)) <= 2.5f);
        Assert.Contains(inkRules, r => Math.Abs(r.Y - (layout.SetAsideHeaderTop + WorksheetRegions.SectionHeaderHeight)) <= 2.5f);
        Assert.Contains(inkRules, r => Math.Abs(r.Y - (layout.AddCategoryHeaderTop + WorksheetRegions.SectionHeaderHeight)) <= 2.5f);
    }

    [Fact]
    public void TheHeaderRuleUnderTheTitleIsOnePointFiveInTheIdentityColour()
    {
        using var document = Open(MockupPlan());
        var identity = PdfProbe.HorizontalRules(document.GetPage(1))
            .Where(r => PdfProbe.SameColor(r.Color, PdfProbe.Rgb(PaperColorTokens.PositiveDoc)) && r.Length > 400)
            .ToList();

        var rule = Assert.Single(identity);
        Assert.Equal(1.5, rule.Width, 0.01);
    }

    [Fact]
    public void EveryRowCarriesAHairline_ThreeSetAsideAndTwoWriteInsIncluded()
    {
        using var document = Open(MockupPlan());
        var hairlines = PdfProbe.HorizontalRules(document.GetPage(1))
            .Where(r => PdfProbe.IsHairline(r) && Math.Abs(r.Width - 1.0) < 0.01 && r.Length > WorksheetRegions.BodyWidth - 2)
            .ToList();

        Assert.Equal(10 + 3 + 2, hairlines.Count);
    }

    [Fact]
    public void LeftToAssignHasAOnePointInkRuleAboveIt()
    {
        using var document = Open(MockupPlan());
        var page = document.GetPage(1);
        var label = page.GetWords().First(w => w.Text == "Left");
        var labelTop = PdfProbe.Box(page, label.BoundingBox).Top;

        var rule = PdfProbe.HorizontalRules(page)
            .Where(r => PdfProbe.IsInk(r) && Math.Abs(r.Width - 1.0) < 0.01 && Math.Abs(r.Length - 270f) < 2f)
            .Single();

        Assert.Equal(WorksheetRegions.MarginLeft, rule.Left, Tolerance);
        Assert.True(rule.Y < labelTop && labelTop - rule.Y < 8f, $"the rule at {rule.Y:0.#} must sit just above 'Left to assign' (top {labelTop:0.#})");
    }

    // ---- nothing collides once the header grew to the Display rung ----

    [Theory]
    [InlineData("2026-10")]
    [InlineData("2027-01")]
    public void NoTwoPaintedWordsOverlap_AndNoWordTouchesABox(string yearMonth)
    {
        var plan = MockupPlan(yearMonth);
        var boxes = WorksheetPainter.BoxesFor(plan);
        using var document = Open(plan);
        var page = document.GetPage(1);
        var words = page.GetWords().Select(w => (Word: w, Rect: PdfProbe.Box(page, w.BoundingBox))).ToList();

        for (var i = 0; i < words.Count; i++)
        {
            for (var j = i + 1; j < words.Count; j++)
            {
                var a = words[i].Rect;
                var b = words[j].Rect;
                var overlap = a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom - 1.5f && b.Top < a.Bottom - 1.5f;
                Assert.False(overlap, $"'{words[i].Word.Text}' and '{words[j].Word.Text}' overlap");
            }
        }

        foreach (var box in boxes)
        {
            foreach (var (word, rect) in words.Where(w => w.Word.Text != "$"))
            {
                var touches = box.Left < rect.Right && rect.Left < box.Left + box.Width && box.Top + 1f < rect.Bottom && rect.Top < box.Top + box.Height - 1f;
                Assert.False(touches, $"box {box.Name} is painted over '{word.Text}'");
            }
        }
    }
}
