using System.Globalization;
using System.Text;
using Index.Domain.Worksheet;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// Index's own instruction string for the shared <c>Storyworthtelling.Handwriting</c> extractor —
/// generically describing "write-in dollar amounts in boxes on a budget worksheet," per this task's
/// brief, rather than copying Foreword's household-name planner-page wording (which describes a
/// different kind of page entirely).
///
/// <para><b>Reuses <see cref="WorksheetRegions"/>'s own coordinates, the same numbers
/// <see cref="WorksheetPainter"/> draws from (Guard G18)</b>: every box is listed by its exact
/// left/top/width/height in points on the 595&#215;842 page, plus, for a category's own box, which
/// printed row (top-to-bottom) it belongs to — so the model can cross-reference by position AND by
/// whatever category name it can itself read printed next to the box, rather than coordinates alone (a
/// vision call reasons far more reliably about "the box next to the printed category name" than about
/// raw point arithmetic on its own).</para>
/// </summary>
public static class WorksheetReadBackInstruction
{
    /// <summary>
    /// Built from the RECORDED category order (<c>BudgetMonth.WorksheetCategoryOrderJson</c>), never
    /// from today's live active-category list — the two can disagree (a category added, renamed or
    /// removed since the page was printed), and it is the recorded order that matches what is actually
    /// drawn on the physical sheet the owner is holding.
    /// </summary>
    public static string Build(IReadOnlyList<Guid> categoryIdsInPrintedOrder, IReadOnlyList<string>? setAsideKeysInPrintedOrder = null)
    {
        // The same layout WorksheetPainter draws from (Guard G18, two-page form): every box comes with
        // the sheet it is on, so a category that spilled onto page two is described where it was painted.
        var layout = WorksheetRegions.Layout(categoryIdsInPrintedOrder, setAsideKeysInPrintedOrder);
        var pageCount = layout.PageCount;
        var sb = new StringBuilder();
        sb.Append(
            "You are reading a household budget worksheet that was printed and then hand-written on " +
            "with a stylus. ");
        if (pageCount == 1)
        {
            sb.Append("It is exactly ");
        }
        else
        {
            sb.Append("It runs to ").Append(pageCount).Append(" pages (the second continues the first); each page is exactly ");
        }
        sb.Append(WorksheetRegions.PageWidth.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" by ").Append(WorksheetRegions.PageHeight.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" points, measured in points from the page's top-left corner. Ignore all printed/typed text ")
            .Append("except where it names a printed category next to a box. Look ONLY at the hand-written ink ")
            .Append("inside the boxes listed below; every box is a small rounded rectangle drawn in ink, most ")
            .Append("with a printed \"$\" beside the writing area. ");

        sb.Append("The boxes, each given as (left, top, width, height) in points on the page named:\n");

        var incomeBox = layout.IncomeCorrection;
        sb.Append("- \"").Append(incomeBox.Name).Append("\" at (")
            .Append(Coords(incomeBox)).Append(") ").Append(OnPage(incomeBox))
            .Append(" — the box beside \"Income different?\", top-right of the page. A whole-dollar figure, ")
            .Append("or blank if the owner did not correct the printed income.\n");

        for (var i = 0; i < layout.Categories.Count; i++)
        {
            var box = layout.Categories[i];
            sb.Append("- \"").Append(box.Name).Append("\" at (").Append(Coords(box)).Append(") ").Append(OnPage(box))
                .Append(" — the limit box on the ").Append(Ordinal(i + 1))
                .Append(pageCount == 1 ? " printed category row from the top" : " printed category row, counting continuously from the top of the first page")
                .Append(", whatever category name is printed immediately to its left. A whole-dollar figure, ")
                .Append("or blank if the owner left last month's figure standing.\n");
        }

        for (var i = 0; i < layout.SetAside.Count; i++)
        {
            var box = layout.SetAside[i];
            sb.Append("- \"").Append(box.Name).Append("\" at (").Append(Coords(box)).Append(") ").Append(OnPage(box))
                .Append(" — the box beside the ").Append(Ordinal(i + 1))
                .Append(" printed line in the \"Set aside\" section, whatever title is printed immediately to ")
                .Append("its left. A whole-dollar figure, or blank if the owner left last month's figure standing.\n");
        }

        foreach (var (nameBox, limitBox) in layout.NewCategories)
        {
            sb.Append("- \"").Append(nameBox.Name).Append("\" at (").Append(Coords(nameBox)).Append(") ").Append(OnPage(nameBox))
                .Append(" — a blank write-in category NAME box (text, not a figure), or blank if unused.\n");
            sb.Append("- \"").Append(limitBox.Name).Append("\" at (").Append(Coords(limitBox)).Append(") ").Append(OnPage(limitBox))
                .Append(" — that write-in category's limit box. A whole-dollar figure, or blank if unused.\n");
        }

        sb.Append(
            "\nRespond with ONLY a JSON array (no prose). One element per box that has ANY hand-written ink in " +
            "it — omit a box entirely if it is blank. Each element: {\"text\": string (the figure with no \"$\" " +
            "sign, or the written name for a \"new-name:*\" box), \"when\": null, \"column\": the box's exact " +
            "name as given above, \"isTask\": false, \"confidence\": number 0..1 for how confident you are in " +
            "the transcription}. Use a low confidence rather than omitting the element when the writing is hard " +
            "to read — a box you attempted but could not make out should still be reported, at low confidence, " +
            "so it can be asked again rather than guessed. If no box carries any ink at all, return [].");

        return sb.ToString();
    }

    private static string OnPage(WorksheetBox box) => $"on page {box.Page + 1}";

    private static string Coords(WorksheetBox box) => string.Create(CultureInfo.InvariantCulture,
        $"{box.Left:0.0}, {box.Top:0.0}, {box.Width:0.0}, {box.Height:0.0}");

    private static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        },
    };
}
