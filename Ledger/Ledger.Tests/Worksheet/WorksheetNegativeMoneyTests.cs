using System.Globalization;
using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using UglyToad.PdfPig;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Phase 4, "Money and type": the printed page writes a negative figure with U+2212 and formats in the
/// invariant en-US shape whatever the phone's language is. Read back out of the PDF, so what is asserted
/// is what the printer would put on paper.
/// </summary>
public class WorksheetNegativeMoneyTests
{
    private const string Minus = "−";

    private static WorksheetPlan NegativePlan() =>
        new(
            "2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, -500, -1_234,
            [
                new WorksheetCategoryLine(Guid.NewGuid(), "Groceries", 548m),
                new WorksheetCategoryLine(Guid.NewGuid(), "Returns", -40m),
            ],
            [
                new WorksheetSetAsideLine(SetAsideLineKind.Savings, WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Emergency fund", -75m),
            ]);

    private static List<string> PrintedWords(WorksheetPlan plan)
    {
        using var document = PdfDocument.Open(new MemoryStream(WorksheetPainter.Paint(plan)));
        return document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).ToList();
    }

    private static List<string> PrintedUnderCulture(string culture, WorksheetPlan plan)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return PrintedWords(plan);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ANegativeLeftToAssignWearsATrueMinus_NotAHyphenAfterTheDollarSign()
    {
        var words = PrintedWords(NegativePlan());

        Assert.Contains(Minus + "$1,234", words);
        Assert.DoesNotContain(words, w => w.Contains("$-", StringComparison.Ordinal));
    }

    [Fact]
    public void ANegativeLastMonthSpendingIsNotPrintedAsAMinusThenAMinus()
    {
        var words = PrintedWords(NegativePlan());

        // Spending of -$500 is a refund: subtracting it adds $500, so the row reads as a plain $500
        // rather than "−$-500" (a minus and then a minus) or a "−$500" that says the opposite.
        Assert.Contains("$500", words);
        Assert.DoesNotContain(words, w => w.Contains(Minus + "$-", StringComparison.Ordinal));
        Assert.DoesNotContain(Minus + "$500", words);
    }

    [Fact]
    public void ANegativeLastMonthFigureOnARowIsPrintedWithATrueMinus()
    {
        var words = PrintedWords(NegativePlan());

        Assert.Contains(Minus + "$40", words);
        Assert.Contains(Minus + "$75", words);
    }

    [Fact]
    public void TheFiguresAreFormattedInvariantlyUnderANonEnglishCulture()
    {
        var words = PrintedUnderCulture("de-DE", NegativePlan());

        Assert.Contains(Minus + "$1,234", words);
        Assert.Contains(Minus + "$6,250", words);
        Assert.Contains("$9,350", words);
        Assert.DoesNotContain(words, w => w.Contains("1.234", StringComparison.Ordinal) || w.Contains("6.250", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOrdinarySubtractionRowStillReadsAsAMinus()
    {
        var plan = NegativePlan() with { LastMonthSpendingWholeDollars = 1_906 };
        var words = PrintedWords(plan);

        Assert.Contains(Minus + "$1,906", words);
    }
}
