using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// R-G12 (INDEX-P11-REMEDIATION-PLAN.md §2.1.1, §10): no writable box on the follow-up page may sit on
/// top of anything printed. The page used to put the two "change another line" boxes a fixed 56pt
/// below the actionable table while the "Everything else, as written" list was drawn from the same
/// anchor, so the boxes were painted over printed figures and the "CHANGE ANOTHER LINE" label landed
/// below the boxes it labels.
///
/// <para>This is asserted on what was actually PAINTED: the page is rendered to a PDF and every glyph's
/// rectangle is read back out with PdfPig, then compared with the boxes the painter reports through
/// <see cref="FollowUpPainter.BoxesFor"/> (the same list it draws from and the read-back prompt is told —
/// Guard G18's principle). A test that only compared two region functions would pass while the painter
/// still ran its own arithmetic.</para>
/// </summary>
public class FollowUpPageGeometryTests
{
    private static FollowUpPlan MakePlan(int referenceLineCount, int actionableCount = 3, bool over = true)
    {
        var actionable = Enumerable.Range(1, actionableCount)
            .Select(i => new FollowUpSuggestion($"category:a{i}", $"Trimmed {i}", null, 100 + i, 200 + i, 100 + i))
            .ToList();
        var reference = Enumerable.Range(1, referenceLineCount)
            .Select(i => new FollowUpSuggestion($"category:r{i}", $"Reference {i}", null, 50 + i, 50 + i, null))
            .ToList();
        return new FollowUpPlan(
            "2026-10", new DateOnly(2026, 10, 2), over, 333, 3_100, 3_433, actionable, reference);
    }

    private sealed record Rect(float Left, float Top, float Right, float Bottom)
    {
        public bool Intersects(Rect other) =>
            Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    }

    private static Rect ToTopLeft(UglyToad.PdfPig.Core.PdfRectangle r) =>
        new((float)r.Left, FollowUpRegions.PageHeight - (float)r.Top, (float)r.Right, FollowUpRegions.PageHeight - (float)r.Bottom);

    private static Rect Of(WorksheetBox b) => new(b.Left, b.Top, b.Left + b.Width, b.Top + b.Height);

    /// <summary>Every painted glyph on the page except the "$" that the painter puts INSIDE a box as its
    /// placeholder — that glyph is meant to be in the box.</summary>
    private static List<(Letter Letter, Rect Rect)> PaintedText(Page page, IReadOnlyList<WorksheetBox> boxes)
    {
        var result = new List<(Letter, Rect)>();
        foreach (var letter in page.Letters)
        {
            if (string.IsNullOrWhiteSpace(letter.Value))
            {
                continue;
            }
            var rect = ToTopLeft(letter.GlyphRectangle);
            var isBoxPlaceholder = letter.Value == "$" && boxes.Any(b => Of(b).Intersects(rect));
            if (!isBoxPlaceholder)
            {
                result.Add((letter, rect));
            }
        }
        return result;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(9)]
    public void NoPaintedBoxIntersectsAnyPaintedTextRun(int referenceLineCount)
    {
        var plan = MakePlan(referenceLineCount);
        var boxes = FollowUpPainter.BoxesFor(plan);

        using var document = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(plan)));
        var text = PaintedText(document.GetPage(1), boxes);

        Assert.NotEmpty(text);
        foreach (var box in boxes)
        {
            foreach (var (letter, rect) in text)
            {
                Assert.False(
                    Of(box).Intersects(rect),
                    $"box {box.Name} ({box.Left:0.#},{box.Top:0.#} {box.Width:0.#}x{box.Height:0.#}) is painted over '{letter.Value}' at {rect.Left:0.#},{rect.Top:0.#} " +
                    $"with {referenceLineCount} reference lines");
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(9)]
    public void LabelThenBoxesThenFooterSentence_InTheMockupsOrder_AllAboveTheFolio(int referenceLineCount)
    {
        var plan = MakePlan(referenceLineCount);
        var boxes = FollowUpPainter.BoxesFor(plan);
        var changeBoxes = boxes.Where(b => b.Name.StartsWith("change-", StringComparison.Ordinal)).ToList();

        using var document = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(plan)));
        var words = document.GetPage(1).GetWords().Select(w => (w.Text, Rect: ToTopLeft(w.BoundingBox))).ToList();

        var label = words.Single(w => w.Text == "CHANGE");
        var firstBoxTop = changeBoxes.Min(b => b.Top);
        var lastBoxBottom = changeBoxes.Max(b => b.Top + b.Height);
        var sentence = words.Single(w => w.Text == "If");
        var folio = words.Single(w => w.Text == "printed");

        Assert.True(label.Rect.Bottom <= firstBoxTop, $"the CHANGE ANOTHER LINE label (bottom {label.Rect.Bottom:0.#}) must sit above the boxes it labels (top {firstBoxTop:0.#})");
        Assert.True(lastBoxBottom <= sentence.Rect.Top, $"the footer sentence (top {sentence.Rect.Top:0.#}) must sit below the boxes (bottom {lastBoxBottom:0.#})");
        Assert.True(sentence.Rect.Bottom <= folio.Rect.Top, "the footer sentence must end above the folio");

        if (referenceLineCount > 0)
        {
            var lastReferenceRow = words.Where(w => w.Text.StartsWith("Reference", StringComparison.Ordinal)).Max(w => w.Rect.Bottom);
            Assert.True(lastReferenceRow <= label.Rect.Top, "the reference list must end above the CHANGE ANOTHER LINE label");
        }
    }

    [Fact]
    public void TheRegionFunctionsThemselvesKeepTheBoxesBelowTheReferenceList()
    {
        // The pure half, so a layout regression reads as a number rather than as a PDF diff: the change
        // boxes start lower the more reference lines there are, and by at least the list's own rows.
        var none = FollowUpRegions.ChangeAnotherLineBoxes(3, 0)[0].Name.Top;
        var two = FollowUpRegions.ChangeAnotherLineBoxes(3, 2)[0].Name.Top;
        var nine = FollowUpRegions.ChangeAnotherLineBoxes(3, 9)[0].Name.Top;

        Assert.True(two > none);
        Assert.True(nine > two);
        Assert.True(nine - two >= 4 * FollowUpRegions.ReferenceRowPitch, "nine lines is five two-column rows against one: four more rows of room");
    }

    [Fact]
    public void ThePaintedPageAndTheReadBackPromptAreToldTheSameBoxes()
    {
        var plan = MakePlan(9);
        var keys = plan.ActionableKeysInPrintedOrder;

        var instruction = FollowUpReadBackInstruction.Build(
            plan.ActionableLines.Select(l => (l.Key, l.Name)).ToList(), plan.ReferenceLines.Count);

        foreach (var box in FollowUpPainter.BoxesFor(plan))
        {
            Assert.Contains($"\"{box.Name}\" at ({box.Left:0.0}, {box.Top:0.0}, {box.Width:0.0}, {box.Height:0.0})", instruction);
        }
        Assert.Equal(keys.Count + 4, FollowUpPainter.BoxesFor(plan).Count);
    }

    [Fact]
    public void AnEmptyReferenceList_PrintsNoHeadingOverNothing()
    {
        using var document = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(MakePlan(0))));
        var text = document.GetPage(1).Text;

        Assert.DoesNotContain("EVERYTHING ELSE", text, StringComparison.Ordinal);
        Assert.Contains("CHANGE ANOTHER LINE", text, StringComparison.Ordinal);

        using var withTwo = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(MakePlan(2))));
        Assert.Contains("EVERYTHING ELSE, AS WRITTEN", withTwo.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWholePageStaysAboveTheFolio_ForTheMockupsOwnCounts()
    {
        // Mockup 13(b): three actionable rows and ten reference lines (five two-column rows).
        var layout = FollowUpRegions.Layout(3, 10);

        Assert.True(layout.FooterSentenceBaseline < FollowUpRegions.PageHeight - FollowUpRegions.MarginBottom,
            $"the footer sentence sits at {layout.FooterSentenceBaseline:0.#}, below the printable body");
        Assert.Equal(5, layout.ReferenceRows);
    }
}
