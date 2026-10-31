using System.Globalization;

namespace Constitution.Domain.Documents;

/// <summary>
/// Pages 2 and 3 of the weekly meal sheet (book p.7): grouped lines, each with a YES box under which "YES" is written, who or what it
/// is for with what is planned beneath it, and the shaded "No, because" field at the other end of the row (D30). A line's boxes come from
/// <see cref="InkTargets"/> and its ids from <see cref="MealSheetIds"/>.
/// </summary>
internal static class MealSheetItemPage
{
    private const float IntroBaseline = 110f;
    private const float FirstHeading = 134f;

    /// <summary>Rows are 50 apart: the 40 point field, then the eight clear points the pen needs, then the "YES" under the box.</summary>
    private const float RowPitch = 50f;

    private const float FieldFromHeading = 12f;
    private const float NextHeadingFromLastRow = 66f;
    private const float YesBoxInset = 11f;
    private const float YesLabelBelowBox = 19f;
    private const float LabelBaselineFromTop = 27f;
    private const float ReasonWidth = 128f;
    private const float LabelColumnWidth = 64f;
    private const float TextLeft = 66f;
    private const float TextGap = 6f;
    private const float LinePitch = 12.5f;
    private const float BlockCentre = 25f;
    private const int TextChars = 22;
    private const int MaxTextLines = 2;

    /// <summary>The lowest the last row's "YES" may reach: just above the footer's ink.</summary>
    private const float ContentLimit = 560f;

    public static InkPageLayout Build(MealSheetModel model, int sheetNumber, string title, string intro, IReadOnlyList<MealSheetGroup> groups)
    {
        var draft = new PageDraft(title);
        draft.Kicker($"{MealSheetDocument.Kind} · {sheetNumber.ToString(CultureInfo.InvariantCulture)} OF {model.Pages.ToString(CultureInfo.InvariantCulture)}");
        draft.Line(PrintedRole.Small, PageDraft.Left, IntroBaseline, PageDraft.FullWidth, intro, name: "intro");

        var heading = FirstHeading;
        foreach (var group in groups)
        {
            draft.Line(PrintedRole.Label, PageDraft.Left, heading, PageDraft.FullWidth, group.Heading, name: group.Items[0].Key + "-heading");
            var top = heading + FieldFromHeading;
            foreach (var item in group.Items)
            {
                Row(draft, item, group.Heading, top);
                top += RowPitch;
            }

            heading = top - RowPitch + NextHeadingFromLastRow;
        }

        MealSheetDocument.Footer(draft, model, sheetNumber);
        return draft.ToLayout();
    }

    private static void Row(PageDraft draft, MealSheetItem item, string group, float top)
    {
        PageDraft.RequireRoom(top + RowPitch + 2f, ContentLimit, $"The {group.ToLowerInvariant()} group (there is no room for '{item.Label}')");

        var yes = InkTargets.TickRows(item.Key, PageDraft.Left, top + YesBoxInset, [item.Meaning])[0] with { Id = item.YesId };
        var reason = InkTargets.NoBecause(item.ReasonId, yes, PageDraft.RightEdge, ReasonWidth);
        draft.Target(yes);
        draft.Target(reason);

        draft.Line(PrintedRole.Label, yes.Bounds.CentreX - 15f, yes.Bounds.Bottom + YesLabelBelowBox, 30f, MealSheetDocument.YesWord, PrintedAlign.Centre, yes.Id, item.Key + "-yes-label");
        draft.Line(
            PrintedRole.Label, reason.Bounds.Left - InkTargets.MinimumClearance - LabelColumnWidth, top + LabelBaselineFromTop, LabelColumnWidth,
            MealSheetDocument.NoBecauseWord, PrintedAlign.Right, reason.Id, item.Key + "-no-label");

        var right = reason.Bounds.Left - InkTargets.MinimumClearance - LabelColumnWidth - TextGap;
        var width = right - TextLeft;
        var lines = PageText.Wrap(item.Text, TextChars);
        if (lines.Count > MaxTextLines)
        {
            throw new ArgumentException(
                $"{item.Label}'s line in '{group}' ('{item.Text}') takes {lines.Count} lines and a row holds {MaxTextLines}; shorten it.", nameof(item));
        }

        var baseline = top + BlockCentre - LinePitch * lines.Count / 2f + 5f;
        draft.Line(PrintedRole.Label, TextLeft, baseline, width, item.Label, name: item.Key + "-person");
        foreach (var line in lines)
        {
            baseline += LinePitch;
            draft.Line(PrintedRole.Body, TextLeft, baseline, width, line, owner: null, name: item.Key + "-line");
        }
    }
}
