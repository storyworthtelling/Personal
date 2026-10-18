using System.Globalization;

namespace Constitution.Domain.Documents;

/// <summary>
/// Assessment week (book p.6): a cover that says what the week is for and that there is no score, then
/// one page per test. A test asks for one number in one open box (unit outside, D15) or one circled word
/// (two to four lanes), and never for a rating (D27): a max-effort test has no "right".
/// </summary>
public static class AssessmentDocument
{
    public const string Name = "Assessment";

    public const string CoverTitle = "Where you start";

    /// <summary>The line that is the point of the week (p.6).</summary>
    public const string NoScoreLine = "There is no score and nothing to beat.";

    private const float FigureHeight = 170f;

    public static IReadOnlyList<InkPageLayout> Build(AssessmentDocumentModel model)
    {
        Validate(model);
        var pages = model.Tests.Count + 1;
        var layouts = new List<InkPageLayout> { Cover(model, pages) };

        for (var i = 0; i < model.Tests.Count; i++)
        {
            layouts.Add(TestPage(model, i, pages));
        }

        return layouts;
    }

    private static InkPageLayout Cover(AssessmentDocumentModel model, int pages)
    {
        var draft = new PageDraft(CoverTitle);
        draft.Kicker($"ASSESSMENT WEEK · DAY {model.Day} OF {model.OfDays} · {PageDraft.DayAndTime(model.Start)}");
        draft.Footer(Name, model.Start, 1, pages);

        var sessions = model.OfDays == 1 ? "One short session" : $"{PageText.Capitalise(PageText.Word(model.OfDays))} short sessions";
        var purpose =
            $"{sessions} this week. Each test asks for one number, written in its box. "
            + $"That number sets where week two begins. {NoScoreLine}";
        var baseline = 118f;
        foreach (var line in PageText.Wrap(purpose, PageText.CoverBodyChars))
        {
            draft.Line(PrintedRole.Body, PageDraft.Left, baseline, PageDraft.FullWidth, line);
            baseline += 16f;
        }

        var rowTop = Math.Max(178f, baseline + 12f);
        draft.Rule("list-top", PageDraft.Left, rowTop, PageDraft.FullWidth, 0f);
        for (var i = 0; i < model.Tests.Count; i++)
        {
            var test = model.Tests[i];
            var row = rowTop + 18f + 34f * i;
            draft.Line(PrintedRole.Small, PageDraft.Left, row, 20f, (i + 1).ToString(CultureInfo.InvariantCulture));
            draft.Line(PrintedRole.Strong, 58f, row, 218f, test.Name);
            draft.Line(PrintedRole.Small, 280f, row, 70f, test.Unit ?? "circle one");
            draft.Line(PrintedRole.Small, PageDraft.RightEdge - 40f, row, 40f, $"p. {i + 2}", PrintedAlign.Right);
            draft.Rule($"list-row-{i + 1}", PageDraft.Left, rowTop + 34f * (i + 1), PageDraft.FullWidth, 0f);
        }

        var note = rowTop + 34f * model.Tests.Count + 32f;
        foreach (var line in PageText.Wrap(model.Schedule, PageText.CoverSmallChars))
        {
            PageDraft.RequireRoom(note, PageDraft.CoverContentLimit, $"An assessment cover for {model.Tests.Count} tests");
            draft.Line(PrintedRole.Small, PageDraft.Left, note, PageDraft.FullWidth, line);
            note += 16f;
        }

        return draft.ToLayout();
    }

    private static InkPageLayout TestPage(AssessmentDocumentModel model, int index, int pages)
    {
        var test = model.Tests[index];
        var draft = new PageDraft(test.Name);
        draft.Kicker($"ASSESSMENT · TEST {index + 1} OF {model.Tests.Count}");
        draft.Footer(Name, model.Start, index + 2, pages);
        draft.NotesColumn();

        var last = 118f - 16f;
        foreach (var line in PageText.Wrap(test.Instruction, PageText.BodyColumnChars))
        {
            last += 16f;
            draft.Line(PrintedRole.Body, PageDraft.Left, last, PageDraft.ColumnWidth, line);
        }

        if (!string.IsNullOrWhiteSpace(test.Hint))
        {
            foreach (var line in PageText.Wrap(test.Hint, PageText.SmallColumnChars))
            {
                last += 16f;
                draft.Line(PrintedRole.Small, PageDraft.Left, last, PageDraft.ColumnWidth, line);
            }
        }

        var y = last;
        var figureBottom = 0f;
        if (test.ShowFigure)
        {
            var slotTop = last + 16f;
            figureBottom = slotTop + FigureHeight;
            draft.Add(new PrintedItem(
                WorkoutIds.FigureSlot, PrintedRole.Slot, new InkRect(PageDraft.Left, slotTop, PageDraft.ColumnWidth, FigureHeight), null, null));
            y = figureBottom;
        }

        float end;
        if (test.Unit is not null)
        {
            var boxTop = y + (test.ShowFigure ? 46f : 30f);
            var box = InkTargets.NumberBox(WorkoutIds.Result, PageDraft.Left, boxTop, test.Name, test.Unit);
            draft.Target(box);
            draft.Line(PrintedRole.Label, PageDraft.Left, boxTop - 12f, PageDraft.ColumnWidth, "WRITE THE NUMBER", owner: box.Id, name: "result-label");
            draft.Add(new PrintedItem("result-unit", PrintedRole.Body, InkTargets.UnitLabelBounds(box, 80f), test.Unit, box.Id));
            end = draft.Voice(test.Encouragement, box.Bounds.Bottom + 46f, box.Bounds.Bottom);
        }
        else
        {
            var lanesTop = test.ShowFigure ? figureBottom + 22f : y + 38f;
            var lanes = draft.Lanes(WorkoutIds.Choice, lanesTop, test.Words!);
            end = draft.Voice(test.Encouragement, lanes[0].Bounds.Bottom + 40f, lanes[0].Bounds.Bottom);
        }

        PageDraft.RequireRoom(end, PageDraft.TestContentLimit, $"The page for {test.Name}");
        draft.DoneTick();
        return draft.ToLayout();
    }

    private static void Validate(AssessmentDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Tests.Count == 0)
        {
            throw new ArgumentException("An assessment needs at least one test.", nameof(model));
        }

        if (model.OfDays < 1 || model.Day < 1 || model.Day > model.OfDays)
        {
            throw new ArgumentException($"Day {model.Day} of {model.OfDays} is not a day of the assessment week.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(model.Schedule))
        {
            throw new ArgumentException("The cover says when the other sessions are; the schedule line cannot be blank.", nameof(model));
        }

        foreach (var test in model.Tests)
        {
            if (string.IsNullOrWhiteSpace(test.Name) || string.IsNullOrWhiteSpace(test.Instruction))
            {
                throw new ArgumentException("A test needs a name and its instruction in words.", nameof(model));
            }

            var wantsNumber = !string.IsNullOrWhiteSpace(test.Unit);
            var wantsWord = test.Words is not null;
            if (wantsNumber == wantsWord)
            {
                throw new ArgumentException(
                    $"The test '{test.Name}' must ask for a number (with its unit) or a circled word, not both and not neither.", nameof(model));
            }

            if (wantsWord && (test.Words!.Count is < 2 or > 4 || test.Words.Any(string.IsNullOrWhiteSpace)))
            {
                throw new ArgumentException($"The test '{test.Name}' needs two to four words to circle.", nameof(model));
            }
        }
    }
}
