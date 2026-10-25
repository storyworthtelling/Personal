using System.Globalization;
using Constitution.Domain.Groceries;

namespace Constitution.Domain.Documents;

/// <summary>
/// The holiday ask-ahead page (book p.7, C-2.4.4), the sheet's last: only the week before a holiday the household hosts or visits.
/// It names the holiday, says when and where from the family calendar, and asks what we are bringing, with dotted lines to write on.
/// Nothing on it is read back: no rule reads a free-text answer yet, so it draws no target.
/// </summary>
internal static class MealSheetAskPage
{
    public const string KickerTail = "ONLY THE WEEK BEFORE A HOLIDAY";

    private const float NameBaseline = 104f;
    private const float WhenBaseline = 116f;
    private const float FirstLine = 150f;
    private const float LinePitch = 18f;
    private const float ParagraphGap = 12f;
    private const float WriteGap = 24f;
    private const float WritePitch = 30f;
    private const int WriteLines = 5;
    private const int WhenChars = 60;
    private const int BodyChars = 58;
    private const float Limit = 556f;

    public static InkPageLayout Build(MealSheetModel model, HolidayAsk ask, int sheetNumber)
    {
        var draft = new PageDraft(ask.Title);
        draft.Kicker($"{MealSheetDocument.Kind} · {sheetNumber.ToString(CultureInfo.InvariantCulture)} OF {model.Pages.ToString(CultureInfo.InvariantCulture)} · {KickerTail}");

        var baseline = WhenBaseline;
        if (ask.Title != ask.Event.Name.Trim())
        {
            var index = 0;
            foreach (var line in PageText.Wrap(ask.Event.Name.Trim(), WhenChars))
            {
                draft.Line(PrintedRole.Strong, PageDraft.Left, NameBaseline + index * LinePitch, PageDraft.FullWidth, line, name: "ask-name-" + ++index);
            }

            baseline = NameBaseline + index * LinePitch + 12f;
        }

        var when = 0;
        foreach (var line in PageText.Wrap(ask.WhenLine, WhenChars))
        {
            draft.Line(PrintedRole.Strong, PageDraft.Left, baseline, PageDraft.FullWidth, line, name: "ask-when-" + ++when);
            baseline += LinePitch;
        }

        baseline = Math.Max(baseline + ParagraphGap, FirstLine);
        var paragraph = 0;
        foreach (var text in ask.Lines)
        {
            paragraph++;
            var number = 0;
            foreach (var line in PageText.Wrap(text, BodyChars))
            {
                draft.Line(PrintedRole.Body, PageDraft.Left, baseline, PageDraft.FullWidth, line, name: $"ask-line-{paragraph}-{++number}");
                baseline += LinePitch;
            }

            baseline += ParagraphGap;
        }

        var write = baseline + WriteGap;
        for (var n = 1; n <= WriteLines && write <= Limit; n++)
        {
            draft.Add(new PrintedItem($"ask-write-{n}", PrintedRole.Dotted, new InkRect(PageDraft.Left, write, PageDraft.FullWidth, 0f), null, null));
            write += WritePitch;
        }

        MealSheetDocument.Footer(draft, model, sheetNumber);
        return draft.ToLayout();
    }
}
