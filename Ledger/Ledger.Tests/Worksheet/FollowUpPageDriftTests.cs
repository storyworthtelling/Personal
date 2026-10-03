using System.Globalization;
using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using Storyworthtelling.Paper;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Phase 4, "Worksheet pages" (mockup 13(b)/(c)): the follow-up page held to its drawing. The mockup's
/// CSS is the source — <c>.fu-head</c>/<c>.fu-row</c> are a grid of <c>1fr 58px 58px 68px 120px</c> with a
/// 10px gap whose number columns are right-aligned, the Final box is the 120px column at 30px high in a
/// 36px row, <c>.fu-rest</c> rows are 19px apart in two columns 28px apart, and the folio date is mono and
/// right-aligned. Everything is read back out of the PDF.
/// </summary>
public class FollowUpPageDriftTests
{
    private const float Tolerance = 1.2f;

    private static FollowUpPlan OverPlan()
    {
        var actionable = new List<FollowUpSuggestion>
        {
            new("category:clothing", "Clothing", null, 104, 248, 104),
            new("category:groceries", "Groceries", null, 548, 690, 548),
            new("category:gifts", "Gifts", null, 55, 158, 111),
        };
        var reference = new[] { ("Dining out", 284), ("Fuel", 162), ("Household", 174), ("Kids", 228), ("Utilities", 286), ("Subscriptions", 68), ("Medical", 85) }
            .Select(r => new FollowUpSuggestion($"category:{r.Item1}", r.Item1, null, r.Item2, r.Item2, null))
            .ToList();
        reference.Add(new FollowUpSuggestion(WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", "Meridian Card", 350, 690, null));
        reference.Add(new FollowUpSuggestion(WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Emergency fund", 225, 225, null));
        reference.Add(new FollowUpSuggestion(WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Summer trip", 135, 135, null));
        return new FollowUpPlan("2026-10", new DateOnly(2026, 10, 2), true, 333, 3_100, 3_433, actionable, reference);
    }

    private static FollowUpPlan UnderPlan()
    {
        var actionable = new List<FollowUpSuggestion>
        {
            new(WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", "Meridian Card", 350, 490, 879),
            new(WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Emergency fund", 225, 225, null),
            new(WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Summer trip", 135, 135, null),
        };
        var reference = new[] { ("Groceries", 565), ("Dining out", 192), ("Fuel", 162) }
            .Select(r => new FollowUpSuggestion($"category:{r.Item1}", r.Item1, null, r.Item2, r.Item2, null))
            .ToList();
        return new FollowUpPlan("2026-10", new DateOnly(2026, 10, 2), false, 389, 3_100, 2_711, actionable, reference);
    }

    private static PdfDocument Open(FollowUpPlan plan) =>
        PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(plan)));

    private static IEnumerable<Word> WordsIn(Page page, string text) => page.GetWords().Where(w => w.Text == text);

    // ---- the grid, in numbers ----

    [Fact]
    public void TheGridNumbersAreTheMockupsOwn()
    {
        Assert.Equal(120f, FollowUpRegions.FinalBoxWidth);
        Assert.Equal(30f, FollowUpRegions.FinalBoxHeight);
        Assert.Equal(37f, FollowUpRegions.RowPitch);          // .fu-row: min-height 36 + a 1px rule
        Assert.Equal(19f, FollowUpRegions.ReferenceRowPitch); // .fu-rest div: line-height 19
        Assert.Equal(WorksheetRegions.MarginLeft + WorksheetRegions.BodyWidth - 120f, FollowUpRegions.FinalBoxLeft, 0.01f);
        Assert.Equal(FollowUpRegions.FinalBoxLeft - 10f, FollowUpRegions.SuggestRight, 0.01f);
        Assert.Equal(FollowUpRegions.SuggestRight - 68f - 10f, FollowUpRegions.WroteRight, 0.01f);
        Assert.Equal(FollowUpRegions.WroteRight - 58f - 10f, FollowUpRegions.PreviousRight, 0.01f);
    }

    [Fact]
    public void EveryFinalBoxIs120By30_AndTheChangeAnotherBoxesAreTheWorksheetsOwnColumn()
    {
        var plan = OverPlan();
        var boxes = FollowUpPainter.BoxesFor(plan);

        var finals = boxes.Where(b => b.Name.StartsWith("final:", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, finals.Count);
        Assert.All(finals, b =>
        {
            Assert.Equal(120f, b.Width);
            Assert.Equal(30f, b.Height);
            Assert.Equal(FollowUpRegions.FinalBoxLeft, b.Left, 0.01f);
        });
        Assert.Equal(37f, finals[1].Top - finals[0].Top, 0.01f);

        var amounts = boxes.Where(b => b.Name.StartsWith("change-amount:", StringComparison.Ordinal)).ToList();
        Assert.All(amounts, b => Assert.Equal(WorksheetRegions.BoxColumnWidth, b.Width));
        Assert.All(boxes.Where(b => b.Name.StartsWith("change-name:", StringComparison.Ordinal)), b => Assert.Equal(WorksheetRegions.NameColumnWidth, b.Width, 0.01f));
    }

    // ---- columns are right-aligned ----

    [Fact]
    public void TheNumberColumnsAndTheirHeadersAreRightAligned()
    {
        using var document = Open(OverPlan());
        var page = document.GetPage(1);

        Assert.Equal(FollowUpRegions.PreviousRight, (float)Assert.Single(WordsIn(page, "SEPT")).BoundingBox.Right, Tolerance);
        Assert.Equal(FollowUpRegions.WroteRight, (float)Assert.Single(WordsIn(page, "WROTE")).BoundingBox.Right, Tolerance);
        Assert.Equal(FollowUpRegions.SuggestRight, (float)Assert.Single(WordsIn(page, "SUGGEST")).BoundingBox.Right, Tolerance);
        Assert.Equal(FollowUpRegions.FinalBoxLeft, (float)Assert.Single(WordsIn(page, "FINAL")).BoundingBox.Left, Tolerance);

        // Clothing 104 / 248 / 104 and Gifts 55 / 158 / 111 — figures of different widths end on one edge.
        Assert.Equal(FollowUpRegions.PreviousRight, (float)Assert.Single(WordsIn(page, "$55")).BoundingBox.Right, Tolerance);
        // $548 is both Groceries' previous figure and its (unchanged) suggestion: one on each edge.
        var groceries = WordsIn(page, "$548").Select(w => (float)w.BoundingBox.Right).OrderBy(r => r).ToList();
        Assert.Equal(2, groceries.Count);
        Assert.Equal(FollowUpRegions.PreviousRight, groceries[0], Tolerance);
        Assert.Equal(FollowUpRegions.SuggestRight, groceries[1], Tolerance);
        Assert.Equal(FollowUpRegions.WroteRight, (float)Assert.Single(WordsIn(page, "$158")).BoundingBox.Right, Tolerance);
        Assert.Equal(FollowUpRegions.SuggestRight, (float)Assert.Single(WordsIn(page, "$111")).BoundingBox.Right, Tolerance);
    }

    [Fact]
    public void TheReferenceListEndsEachColumnOnItsOwnRightEdge_AndRowsAreNineteenApart()
    {
        using var document = Open(OverPlan());
        var page = document.GetPage(1);

        // Row 0 is Dining out | Fuel's neighbour... the list fills across then down: (Dining out, Fuel),
        // (Household, Kids), (Utilities, Subscriptions) ...
        Assert.Equal(FollowUpRegions.ReferenceColumnRight(0), (float)Assert.Single(WordsIn(page, "$284")).BoundingBox.Right, Tolerance);
        Assert.Equal(FollowUpRegions.ReferenceColumnRight(1), (float)Assert.Single(WordsIn(page, "$162")).BoundingBox.Right, Tolerance);

        var diningBaseline = PdfProbe.Box(page, Assert.Single(WordsIn(page, "Dining")).BoundingBox).Bottom;
        var householdBaseline = PdfProbe.Box(page, Assert.Single(WordsIn(page, "Household")).BoundingBox).Bottom;
        Assert.Equal(19f, householdBaseline - diningBaseline, 1.5f);
    }

    // ---- title, kicker, the previous month ----

    [Fact]
    public void TheTitleIsSetAtTheDisplayRung_AndClearsTheHeaderRule()
    {
        using var document = Open(OverPlan());
        var page = document.GetPage(1);
        var title = page.Letters.Where(l => l.PointSize > 20).ToList();

        Assert.NotEmpty(title);
        Assert.All(title, l => Assert.Equal(PaperTypeScale.Display, (float)l.PointSize, 0.3f));
        var rule = PdfProbe.HorizontalRules(page).First(r => Math.Abs(r.Width - 1.5) < 0.01 && r.Length > 400);
        Assert.True(title.Max(l => PdfProbe.Box(page, l.GlyphRectangle).Bottom) < rule.Top);
    }

    [Fact]
    public void TheHeadingAndTheIntroNameThePreviousMonth()
    {
        using var document = Open(OverPlan());
        var text = document.GetPage(1).Text;

        Assert.Contains("SEPT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LAST MONTH", text, StringComparison.Ordinal);
        Assert.Contains("furthest above September's spending", text, StringComparison.Ordinal);
        Assert.DoesNotContain("above last month's", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamesAreDerivedFromTheMonth()
    {
        var plan = OverPlan() with { TargetYearMonth = "2027-01" };
        using var document = Open(plan);
        var text = document.GetPage(1).Text;

        Assert.Contains("furthest above December's spending", text, StringComparison.Ordinal);
        Assert.Contains("DEC", text, StringComparison.Ordinal);
    }

    // ---- "Extra payment", with the loan's name ----

    [Fact]
    public void TheExtraPaymentRowIsTitledExtraPayment_AndNamesItsLoan()
    {
        using var document = Open(UnderPlan());
        var page = document.GetPage(1);

        var title = Assert.Single(WordsIn(page, "Extra"));
        var loan = Assert.Single(WordsIn(page, "Meridian"));
        Assert.DoesNotContain("Extra debt", page.Text, StringComparison.Ordinal);
        Assert.Contains("Extra payment", page.Text, StringComparison.Ordinal);
        // The loan's name is the small line directly under the title.
        Assert.True(PdfProbe.Box(page, loan.BoundingBox).Top >= PdfProbe.Box(page, title.BoundingBox).Bottom - 2f);
        Assert.Equal(PdfProbe.Box(page, title.BoundingBox).Left, PdfProbe.Box(page, loan.BoundingBox).Left, Tolerance);
    }

    [Fact]
    public void TheReferenceListStillCallsItExtraDebtPayment()
    {
        // Mockup 13(b): the actionable table says "Extra payment / Meridian Card"; the reference list says
        // "Extra debt payment" for the same line.
        using var document = Open(OverPlan());

        Assert.Contains("Extra debt payment", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadBackIsToldTheTitleThePageActuallyPrints()
    {
        var line = UnderPlan().ActionableLines[0];

        Assert.Equal("Extra payment", line.TableTitle);
        Assert.Equal("Extra payment (Meridian Card)", line.PrintedName);
        Assert.Equal("Extra debt payment", line.ReferenceLabel);
    }

    // ---- the footer ----

    [Fact]
    public void TheFooterDateIsMono_RightAligned_AndInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            string[] months = ["jan.", "fév.", "mar.", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc.", ""];
            odd.DateTimeFormat.AbbreviatedMonthNames = months;
            odd.DateTimeFormat.AbbreviatedMonthGenitiveNames = months;
            CultureInfo.CurrentCulture = odd;

            using var document = Open(OverPlan());
            var page = document.GetPage(1);
            var date = page.GetWords().Where(w => w.Text is "printed" or "Oct" or "2").ToList();

            Assert.Equal(3, date.Count);
            Assert.Equal(WorksheetRegions.MarginLeft + WorksheetRegions.BodyWidth, (float)date.Single(w => w.Text == "2").BoundingBox.Right, Tolerance);
            Assert.All(date, w => Assert.Contains("Cascadia", w.Letters[0].FontName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("oct.", page.Text, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- the rules ----

    [Fact]
    public void HeadersCarryHeavyRules_AndRowsCarryHairlines()
    {
        var plan = OverPlan();
        using var document = Open(plan);
        var rules = PdfProbe.HorizontalRules(document.GetPage(1));
        var full = WorksheetRegions.BodyWidth - 2;

        var identity = rules.Where(r => PdfProbe.SameColor(r.Color, PdfProbe.Rgb(PaperColorTokens.PositiveDoc)) && r.Length > full).ToList();
        Assert.Equal(1.5, Assert.Single(identity).Width, 0.01);

        // LINE header, EVERYTHING ELSE, CHANGE ANOTHER LINE.
        var heavy = rules.Where(r => PdfProbe.IsInk(r) && Math.Abs(r.Width - 1.5) < 0.01 && r.Length > full).ToList();
        Assert.Equal(3, heavy.Count);

        // Three actionable rows and two change-another rows, full width.
        var rowRules = rules.Where(r => PdfProbe.IsHairline(r) && Math.Abs(r.Width - 1.0) < 0.01 && r.Length > full).ToList();
        Assert.Equal(3 + FollowUpRegions.ChangeAnotherLines, rowRules.Count);

        // One per reference line, each as wide as its column.
        var columnRules = rules.Where(r => PdfProbe.IsHairline(r) && Math.Abs(r.Width - 1.0) < 0.01 && Math.Abs(r.Length - FollowUpRegions.ReferenceColumnWidth) < 2f).ToList();
        Assert.Equal(plan.ReferenceLines.Count, columnRules.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(10)]
    public void NothingOverlapsOnTheTallerRows(int referenceCount)
    {
        var baseline = UnderPlan();
        var reference = Enumerable.Range(1, referenceCount)
            .Select(i => new FollowUpSuggestion($"category:r{i}", $"Reference {i}", null, 50 + i, 50 + i, null)).ToList();
        var plan = baseline with { ReferenceLines = reference };
        var boxes = FollowUpPainter.BoxesFor(plan);
        using var document = Open(plan);
        var page = document.GetPage(1);
        var words = page.GetWords().Select(w => (Word: w, Rect: PdfProbe.Box(page, w.BoundingBox))).ToList();

        for (var i = 0; i < words.Count; i++)
        {
            for (var j = i + 1; j < words.Count; j++)
            {
                var a = words[i].Rect;
                var b = words[j].Rect;
                Assert.False(a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom - 1.5f && b.Top < a.Bottom - 1.5f,
                    $"'{words[i].Word.Text}' and '{words[j].Word.Text}' overlap");
            }
        }
        foreach (var box in boxes)
        {
            foreach (var (word, rect) in words.Where(w => w.Word.Text != "$"))
            {
                Assert.False(
                    box.Left < rect.Right && rect.Left < box.Left + box.Width && box.Top + 1f < rect.Bottom && rect.Top < box.Top + box.Height - 1f,
                    $"box {box.Name} is painted over '{word.Text}'");
            }
        }
        Assert.True(FollowUpRegions.Layout(plan.ActionableLines.Count, referenceCount).FooterSentenceBaseline < FollowUpRegions.PageHeight - FollowUpRegions.MarginBottom);
    }
}
