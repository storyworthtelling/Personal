namespace Index.Domain.Worksheet;

/// <summary>
/// Where every writable box on the follow-up page sits (mockup 13(b)/(c), Release H), in points, on the
/// same A4 page <see cref="WorksheetRegions"/> uses. As with the worksheet, both <c>FollowUpPainter</c>
/// and the follow-up's own read-back instruction call the methods below — neither hardcodes a box
/// position of its own, the same Guard-G18-style discipline the worksheet's boxes already follow.
///
/// <para>The mockup's own four-column table (Sept / Wrote / Suggest / Final) is reference text drawn
/// beside each box; only the FINAL column is ever writable — the box the owner's own figure (or, left
/// empty, the printed suggestion, D156) goes into.</para>
/// </summary>
public static class FollowUpRegions
{
    public const float PageWidth = WorksheetRegions.PageWidth;
    public const float PageHeight = WorksheetRegions.PageHeight;
    public const float MarginLeft = WorksheetRegions.MarginLeft;
    public const float MarginRight = WorksheetRegions.MarginRight;
    public const float MarginTop = WorksheetRegions.MarginTop;
    public const float MarginBottom = WorksheetRegions.MarginBottom;
    public const float BodyWidth = WorksheetRegions.BodyWidth;

    public const float HeaderHeight = 54f;
    public const float IntroHeight = 44f;
    public const float TableHeaderHeight = 20f;

    public const float RowPitch = 34f;
    public const float BoxWidth = 90f;
    public const float BoxHeight = 26f;

    public const int ChangeAnotherLines = 2;

    // ---- the vertical flow below the actionable table (Guard R-G12) ----
    // The page runs, top to bottom: the actionable table, the "With the suggestion(s)" line, the
    // "Everything else, as written" list (two columns, however many lines there are), the "Change another
    // line" label, its two rows of boxes, and the footer sentence. Each starts below where the last one
    // ENDS, and how far the list runs depends on how many lines it holds — so the position of every
    // later block is computed from the list's height, in Layout, and nowhere else. (The boxes used to sit
    // a fixed 56pt under the table while the list was drawn from the same anchor, so they were painted
    // over printed figures.)

    /// <summary>Baseline of the "With the suggestion(s)" line, below the last table row.</summary>
    public const float WithSuggestionsOffset = 16f;

    /// <summary>From the "With the suggestion(s)" baseline to the "Everything else" heading's baseline.</summary>
    public const float ReferenceHeaderOffset = 24f;

    /// <summary>One row of the reference list; the list is <see cref="ReferenceColumns"/> columns wide.</summary>
    public const float ReferenceRowPitch = 16f;
    public const int ReferenceColumns = 2;

    /// <summary>From the last printed line above (the last reference row, or the "With the suggestion(s)"
    /// line when the list is empty) to the "Change another line" label's baseline.</summary>
    public const float ChangeHeaderGap = 28f;

    /// <summary>From the label's baseline to the top of the first row of boxes.</summary>
    public const float ChangeHeaderToBoxes = 10f;

    /// <summary>From the bottom of the last box to the footer sentence's baseline.</summary>
    public const float BoxesToFooterSentence = 22f;

    public static string FinalBox(string lineKey) => $"final:{lineKey}";
    public static string ChangeAnotherName(int index) => $"change-name:{index}";
    public static string ChangeAnotherAmount(int index) => $"change-amount:{index}";

    public static float TableTop => MarginTop + HeaderHeight + IntroHeight + TableHeaderHeight;

    public static WorksheetBox ActionableBox(int index, string lineKey) =>
        new(FinalBox(lineKey), MarginLeft + BodyWidth - BoxWidth, TableTop + index * RowPitch, BoxWidth, BoxHeight);

    public static IReadOnlyList<WorksheetBox> ActionableBoxes(IReadOnlyList<string> lineKeysInPrintedOrder)
    {
        var boxes = new List<WorksheetBox>(lineKeysInPrintedOrder.Count);
        for (var i = 0; i < lineKeysInPrintedOrder.Count; i++)
        {
            boxes.Add(ActionableBox(i, lineKeysInPrintedOrder[i]));
        }
        return boxes;
    }

    /// <summary>
    /// Where each block below the actionable table sits for a page with
    /// <paramref name="actionableLineCount"/> actionable rows and <paramref name="referenceLineCount"/>
    /// lines in "Everything else, as written". The one place this vertical flow is decided:
    /// <c>FollowUpPainter</c> draws from it and the box functions below read it, so the label, the boxes
    /// and the sentence can never be placed by two different sums.
    /// </summary>
    public static FollowUpLayout Layout(int actionableLineCount, int referenceLineCount)
    {
        var withSuggestions = TableTop + actionableLineCount * RowPitch + WithSuggestionsOffset;
        var referenceRows = (referenceLineCount + ReferenceColumns - 1) / ReferenceColumns;

        float? referenceHeader = null;
        var referenceFirstRow = 0f;
        var lastPrintedLine = withSuggestions;
        if (referenceLineCount > 0)
        {
            // An empty list prints no heading at all: a heading over nothing is a table of nothing.
            referenceHeader = withSuggestions + ReferenceHeaderOffset;
            referenceFirstRow = referenceHeader.Value + TableHeaderHeight;
            lastPrintedLine = referenceFirstRow + (referenceRows - 1) * ReferenceRowPitch;
        }

        var changeHeader = lastPrintedLine + ChangeHeaderGap;
        var boxesTop = changeHeader + ChangeHeaderToBoxes;
        var lastBoxBottom = boxesTop + (ChangeAnotherLines - 1) * RowPitch + BoxHeight;

        return new FollowUpLayout(
            withSuggestions, referenceHeader, referenceFirstRow, referenceRows,
            changeHeader, boxesTop, lastBoxBottom + BoxesToFooterSentence);
    }

    public static IReadOnlyList<(WorksheetBox Name, WorksheetBox Amount)> ChangeAnotherLineBoxes(
        int actionableLineCount, int referenceLineCount)
    {
        var top0 = Layout(actionableLineCount, referenceLineCount).ChangeBoxesTop;
        var result = new List<(WorksheetBox, WorksheetBox)>(ChangeAnotherLines);
        for (var i = 0; i < ChangeAnotherLines; i++)
        {
            var top = top0 + i * RowPitch;
            var name = new WorksheetBox(
                ChangeAnotherName(i), MarginLeft, top, BodyWidth - BoxWidth - 12f, BoxHeight);
            var amount = new WorksheetBox(
                ChangeAnotherAmount(i), MarginLeft + BodyWidth - BoxWidth, top, BoxWidth, BoxHeight);
            result.Add((name, amount));
        }
        return result;
    }

    /// <summary>Every writable box on the page, in printed order: one Final box per actionable line,
    /// then the two "change another line" (name, figure) pairs.</summary>
    public static IReadOnlyList<WorksheetBox> AllBoxes(
        IReadOnlyList<string> actionableLineKeysInPrintedOrder, int referenceLineCount)
    {
        var all = new List<WorksheetBox>();
        all.AddRange(ActionableBoxes(actionableLineKeysInPrintedOrder));
        foreach (var (name, amount) in ChangeAnotherLineBoxes(actionableLineKeysInPrintedOrder.Count, referenceLineCount))
        {
            all.Add(name);
            all.Add(amount);
        }
        return all;
    }
}

/// <summary>The Y positions (page points, from the top) of everything below the actionable table, as
/// <see cref="FollowUpRegions.Layout"/> computed them. Baselines for text, tops for boxes.
/// <see cref="ReferenceHeaderBaseline"/> is null when there are no reference lines (no heading is
/// printed then).</summary>
public sealed record FollowUpLayout(
    float WithSuggestionsBaseline,
    float? ReferenceHeaderBaseline,
    float ReferenceFirstRowBaseline,
    int ReferenceRows,
    float ChangeHeaderBaseline,
    float ChangeBoxesTop,
    float FooterSentenceBaseline);
