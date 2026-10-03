using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// R-G25's page half (D49; mockup 13's follow-up anatomy): the page that asks for an unread limit again,
/// drawn through the real painter and read back with PdfPig. It must say what it is for, show "not read"
/// where a figure would be (never a "$0" that claims a reading), keep every box clear of every printed
/// run (R-G12, with the new rows on the page), and stay inside the margins.
/// </summary>
public class UnreadableResendPageTests
{
    private static FollowUpSuggestion Unread(string name, string? detail = null, string key = "category:u1") =>
        new(key, name, detail, 196, 0, null, WasUnreadable: true);

    private static FollowUpPlan ResendOnly(int unreadCount = 1, int referenceCount = 3, bool hasTarget = true)
    {
        var actionable = Enumerable.Range(1, unreadCount).Select(i => Unread($"Dining out {i}", key: $"category:u{i}")).ToList();
        var reference = Enumerable.Range(1, referenceCount)
            .Select(i => new FollowUpSuggestion($"category:r{i}", $"Reference {i}", null, 50 + i, 50 + i, null)).ToList();
        return new FollowUpPlan("2026-10", new DateOnly(2026, 10, 2), false, 0, 3_100, 3_100, actionable, reference, hasTarget);
    }

    private static FollowUpPlan Mixed(int referenceCount = 3)
    {
        var actionable = new List<FollowUpSuggestion>
        {
            Unread("Dining out"),
            new("category:a1", "Clothing", null, 104, 248, 104),
            new("category:a2", "Gifts", null, 55, 158, 111),
        };
        var reference = Enumerable.Range(1, referenceCount)
            .Select(i => new FollowUpSuggestion($"category:r{i}", $"Reference {i}", null, 50 + i, 50 + i, null)).ToList();
        return new FollowUpPlan("2026-10", new DateOnly(2026, 10, 2), true, 333, 3_100, 3_433, actionable, reference);
    }

    private static string Text(FollowUpPlan plan)
    {
        using var document = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(plan)));
        return document.GetPage(1).Text;
    }

    [Fact]
    public void AResendOnlyPage_SaysWhatItIsFor_AndMakesNoOverOrUnderClaim()
    {
        var text = Text(ResendOnly());

        Assert.Contains("1 limit to write again", text, StringComparison.Ordinal);
        Assert.Contains("couldn't be read", text, StringComparison.Ordinal);
        Assert.Contains("not read", text, StringComparison.Ordinal);
        Assert.Contains("If a limit still can't be read, another page like this comes tomorrow.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("With the suggestion", text, StringComparison.Ordinal);
        Assert.DoesNotContain("to place", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ManyUnreadLines_AreCounted_NotJustNamed()
    {
        var text = Text(ResendOnly(unreadCount: 3));

        Assert.Contains("3 limits to write again", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANoTargetPage_StillAsksForTheLimit_AndSaysNothingAboutBalancing()
    {
        var text = Text(ResendOnly(hasTarget: false));

        Assert.Contains("1 limit to write again", text, StringComparison.Ordinal);
        Assert.DoesNotContain("balanced", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnreadRowPrintsNotReadWhereTheFigureWouldBe_AndNeverADollarZero()
    {
        var text = Text(ResendOnly());

        Assert.Contains("not read", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverPageThatAlsoCarriesAnUnreadLine_KeepsItsSuggestions_AndSaysWhatTheUnreadLineNeeds()
    {
        var text = Text(Mixed());

        Assert.Contains("$333 over", text, StringComparison.Ordinal);
        Assert.Contains("With the suggestions", text, StringComparison.Ordinal);
        Assert.Contains("not read", text, StringComparison.Ordinal);
        Assert.Contains("a line marked not read needs your figure again", text, StringComparison.Ordinal);
        Assert.Contains("or a limit can't be read", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoUnreadLine_ThePageIsExactlyTheOneItAlwaysWas()
    {
        var plain = new FollowUpPlan(
            "2026-10", new DateOnly(2026, 10, 2), true, 333, 3_100, 3_433,
            [new FollowUpSuggestion("category:a1", "Clothing", null, 104, 248, 104)], []);

        var text = Text(plain);

        Assert.DoesNotContain("not read", text, StringComparison.Ordinal);
        Assert.Contains("Leave a box empty to take the suggestion, or write your own figure.", text, StringComparison.Ordinal);
        Assert.Contains("If the final figures still don't come to $3,100, another page like this comes tomorrow.", text, StringComparison.Ordinal);
    }

    // ---- R-G12 with the new rows: nothing painted over, nothing past the margins ----

    private sealed record Rect(float Left, float Top, float Right, float Bottom)
    {
        public bool Intersects(Rect other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    }

    private static Rect ToTopLeft(UglyToad.PdfPig.Core.PdfRectangle r) =>
        new((float)r.Left, FollowUpRegions.PageHeight - (float)r.Top, (float)r.Right, FollowUpRegions.PageHeight - (float)r.Bottom);

    private static Rect Of(WorksheetBox b) => new(b.Left, b.Top, b.Left + b.Width, b.Top + b.Height);

    public static TheoryData<string, int> Shapes => new()
    {
        { "resend-only", 0 }, { "resend-only", 2 }, { "resend-only", 9 },
        { "mixed", 0 }, { "mixed", 2 }, { "mixed", 9 },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void NoBoxIsPaintedOverText_AndNoTextRunsPastTheRightMargin(string shape, int referenceCount)
    {
        var plan = shape == "resend-only" ? ResendOnly(unreadCount: 2, referenceCount: referenceCount) : Mixed(referenceCount);
        var boxes = FollowUpPainter.BoxesFor(plan);

        using var document = PdfDocument.Open(new MemoryStream(FollowUpPainter.Paint(plan)));
        var page = document.GetPage(1);
        var letters = page.Letters
            .Where(l => !string.IsNullOrWhiteSpace(l.Value))
            .Select(l => (Letter: l, Rect: ToTopLeft(l.GlyphRectangle)))
            .Where(x => !(x.Letter.Value == "$" && boxes.Any(b => Of(b).Intersects(x.Rect))))
            .ToList();

        Assert.NotEmpty(letters);
        foreach (var box in boxes)
        {
            foreach (var (letter, rect) in letters)
            {
                Assert.False(Of(box).Intersects(rect), $"box {box.Name} is painted over '{letter.Value}' ({shape}, {referenceCount} reference lines)");
            }
        }

        var rightEdge = FollowUpRegions.PageWidth - FollowUpRegions.MarginRight + 1f;
        foreach (var (letter, rect) in letters)
        {
            Assert.True(rect.Right <= rightEdge, $"'{letter.Value}' runs to {rect.Right:0.#}, past the right margin ({shape})");
        }
    }

    [Fact]
    public void TheUnreadRowGetsItsOwnFinalBox_AmongTheOnesThePainterAndTheReaderShare()
    {
        var plan = ResendOnly(unreadCount: 2);

        var names = FollowUpPainter.BoxesFor(plan).Select(b => b.Name).ToList();

        Assert.Contains("final:category:u1", names);
        Assert.Contains("final:category:u2", names);
        Assert.Equal(plan.ActionableKeysInPrintedOrder.Count + 4, names.Count);
    }

    [Fact]
    public void TheInstructionNamesTheUnreadRowAsAskedAgain_AndOnlyThatRow()
    {
        var plan = Mixed();
        var instruction = FollowUpReadBackInstruction.Build(
            plan.ActionableLines.Select(l => (l.Key, l.PrintedName)).ToList(), plan.ReferenceLines.Count,
            plan.ResendLines.Select(l => l.Key).ToHashSet());

        var lines = instruction.Split('\n');
        var unread = lines.Single(l => l.Contains("final:category:u1", StringComparison.Ordinal));
        var ordinary = lines.Single(l => l.Contains("final:category:a1", StringComparison.Ordinal));

        Assert.Contains("NO printed suggestion", unread, StringComparison.Ordinal);
        Assert.DoesNotContain("NO printed suggestion", ordinary, StringComparison.Ordinal);
    }

    // ---- a page left over from an earlier month is not this month's ----

    [Fact]
    public void TheReadBackIsToldTheHeadingToCheck_AndThePageActuallyPrintsThatHeading()
    {
        // The follow-up is one fixed-title document each new page replaces, so the page left from last
        // month's cycle is normally still on the tablet when this month's first morning arrives. Reading
        // it with this month's box coordinates would apply last month's ink to this month's boxes — and,
        // for a resend row, clear the unread flag with it. The reader is told to refuse a page whose
        // heading names another month, and the heading it is told to look for is the painter's own.
        var plan = Mixed();
        var instruction = FollowUpReadBackInstruction.Build(
            plan.ActionableLines.Select(l => (l.Key, l.PrintedName)).ToList(), plan.ReferenceLines.Count,
            plan.ResendLines.Select(l => l.Key).ToHashSet(), plan.TargetYearMonth);

        var heading = FollowUpPainter.Heading(plan.TargetYearMonth);

        Assert.Equal("INDEX · OCTOBER 2026 · FOLLOW-UP", heading);
        Assert.Contains(heading, instruction, StringComparison.Ordinal);
        Assert.Contains("respond with []", instruction, StringComparison.Ordinal);
        Assert.Contains(heading, Text(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAMonth_TheInstructionIsExactlyWhatItWas()
    {
        var instruction = FollowUpReadBackInstruction.Build([("category:a", "Gifts")], 0);

        Assert.DoesNotContain("heading", instruction, StringComparison.Ordinal);
    }
}
