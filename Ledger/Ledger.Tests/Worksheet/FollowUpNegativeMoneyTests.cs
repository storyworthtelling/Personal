using System.Globalization;
using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using UglyToad.PdfPig;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// The follow-up page prints money the way the worksheet does: a true minus for a negative figure and the
/// invariant en-US shape whatever the phone's language is.
/// </summary>
public class FollowUpNegativeMoneyTests
{
    private const string Minus = "−";

    private static FollowUpPlan NegativePlan() =>
        new(
            "2026-10", new DateOnly(2026, 10, 2), false, -333, 3_100, 3_433,
            [new FollowUpSuggestion("category:returns", "Returns", null, -45, 1_250, 1_250)],
            [new FollowUpSuggestion("category:fuel", "Fuel", null, 162, 162, null)]);

    private static List<string> PrintedUnder(string culture, FollowUpPlan plan)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var document = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(plan)));
            return document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).ToList();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ANegativeLastMonthFigureWearsATrueMinus()
    {
        var words = PrintedUnder("en-US", NegativePlan());

        Assert.Contains(Minus + "$45", words);
        Assert.DoesNotContain(words, w => w.Contains("$-", StringComparison.Ordinal));
    }

    [Fact]
    public void ANegativeDifferenceInTheHeadlineWearsATrueMinus()
    {
        var words = PrintedUnder("en-US", NegativePlan());

        Assert.Contains(Minus + "$333", words);
    }

    [Fact]
    public void TheFiguresAreFormattedInvariantlyUnderANonEnglishCulture()
    {
        var words = PrintedUnder("de-DE", NegativePlan());

        Assert.Contains("$3,100", words.Concat(words.Select(w => w.TrimEnd(',', '.'))));
        Assert.Contains("$1,250", words);
        Assert.Contains(Minus + "$45", words);
        Assert.DoesNotContain(words, w => w.Contains("1.250", StringComparison.Ordinal) || w.Contains("3.100", StringComparison.Ordinal));
    }
}
