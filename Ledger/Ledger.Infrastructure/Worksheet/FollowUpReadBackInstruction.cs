using System.Globalization;
using System.Text;
using Index.Domain.Worksheet;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// The follow-up page's own instruction string for the shared <c>Storyworthtelling.Handwriting</c>
/// extractor, mirroring <see cref="WorksheetReadBackInstruction"/>'s shape but describing
/// <see cref="FollowUpRegions"/>'s own boxes — one Final box per actionable line, plus the two
/// "change another line" (name, figure) pairs.
/// </summary>
public static class FollowUpReadBackInstruction
{
    /// <param name="targetYearMonth">The month this page was printed for. The follow-up is one fixed-title
    /// document that each new page replaces, so a page from an EARLIER month's cycle can still be sitting
    /// on the tablet when a new month's first morning comes round; the reader is told to check the heading
    /// and report nothing from a page that names another month, rather than have an old figure read into a
    /// new month's box.</param>
    /// <param name="unreadableKeys">The boxed lines that are there only because their limit could not be
    /// read (D49): each is described as a line to be written again, with no printed suggestion to fall
    /// back on, so a blank box is reported as blank rather than guessed.</param>
    public static string Build(
        IReadOnlyList<(string Key, string Name)> actionableLinesInPrintedOrder, int referenceLineCount,
        IReadOnlySet<string>? unreadableKeys = null,
        string? targetYearMonth = null)
    {
        var keys = actionableLinesInPrintedOrder.Select(l => l.Key).ToList();
        var sb = new StringBuilder();
        sb.Append(
            "You are reading a follow-up budget page that was printed and then hand-written on with a ")
            .Append("stylus. It is exactly ").Append(FollowUpRegions.PageWidth.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" by ").Append(FollowUpRegions.PageHeight.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" points, measured in points from the page's top-left corner. Ignore all printed/typed text ")
            .Append("except where it names a printed line next to a box. Look ONLY at the hand-written ink inside ")
            .Append("the boxes listed below; every box is a small rounded rectangle drawn in ink, most with a ")
            .Append("printed \"$\" beside the writing area. ");

        if (targetYearMonth is not null)
        {
            sb.Append("First check the small heading at the top-left of the page: it must read \"")
                .Append(FollowUpPainter.Heading(targetYearMonth))
                .Append("\". If it names a different month, this is an old page left on the tablet and not the one ")
                .Append("being read: respond with [] and nothing else. ");
        }

        sb.Append("The boxes, each given as (left, top, width, height) in points:\n");

        var boxes = FollowUpRegions.ActionableBoxes(keys);
        for (var i = 0; i < actionableLinesInPrintedOrder.Count; i++)
        {
            var box = boxes[i];
            var again = unreadableKeys?.Contains(actionableLinesInPrintedOrder[i].Key) == true;
            sb.Append("- \"").Append(box.Name).Append("\" at (").Append(Coords(box))
                .Append(") — the FINAL box on the row printed for \"").Append(actionableLinesInPrintedOrder[i].Name)
                .Append(again
                    ? "\", marked \"not read\": its limit could not be read last time and is being asked for again. " +
                      "A whole-dollar figure, or blank if the owner has not written it yet. There is NO printed suggestion for this row.\n"
                    : "\". A whole-dollar figure, or blank if the owner left the printed suggestion standing.\n");
        }

        foreach (var (nameBox, amountBox) in FollowUpRegions.ChangeAnotherLineBoxes(actionableLinesInPrintedOrder.Count, referenceLineCount))
        {
            sb.Append("- \"").Append(nameBox.Name).Append("\" at (").Append(Coords(nameBox))
                .Append(") — a blank write-in LINE NAME box under \"Change another line\" (text, not a figure), ")
                .Append("or blank if unused.\n");
            sb.Append("- \"").Append(amountBox.Name).Append("\" at (").Append(Coords(amountBox))
                .Append(") — that line's own figure. A whole-dollar figure, or blank if unused.\n");
        }

        sb.Append(
            "\nRespond with ONLY a JSON array (no prose). One element per box that has ANY hand-written ink in " +
            "it — omit a box entirely if it is blank. Each element: {\"text\": string (the figure with no \"$\" " +
            "sign, or the written name for a \"change-name:*\" box), \"when\": null, \"column\": the box's exact " +
            "name as given above, \"isTask\": false, \"confidence\": number 0..1 for how confident you are in " +
            "the transcription}. Use a low confidence rather than omitting the element when the writing is hard " +
            "to read — a box you attempted but could not make out should still be reported, at low confidence, " +
            "so it can be asked again rather than guessed. If no box carries any ink at all, return [].");

        return sb.ToString();
    }

    private static string Coords(WorksheetBox box) => string.Create(CultureInfo.InvariantCulture,
        $"{box.Left:0.0}, {box.Top:0.0}, {box.Width:0.0}, {box.Height:0.0}");
}
