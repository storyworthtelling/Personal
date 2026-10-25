using System.Globalization;
using Constitution.Domain.Groceries;

namespace Constitution.Domain.Documents;

/// <summary>One standing staple's box on the groceries page: the id it is answered under, the name on the paper, and the words a rule names it by.</summary>
internal sealed record MealSheetStaple(string Id, string Name, Aisle Aisle);

/// <summary>
/// The groceries page and the staples to tick (book p.7, C-2.4.3): the budget in words (framed in ink when the week runs over), the week's
/// list by store section with quantities, a plain box for every standing staple ("need more": ticked is a need, blank is none), and blank
/// lines for new ones. The list is the preview only; the Keep note is the separate hand-off (D81, D90). The content flows: whatever does not
/// fit continues on further pages, with the sheet's "x of N" counting them.
/// </summary>
internal static class MealSheetGroceryPages
{
    public const string Title = "Groceries";
    public const string ContinuedTitle = "Groceries, continued";
    public const string ListHeading = "THIS WEEK’S GROCERIES · BY STORE SECTION";
    public const string PreviewLine = "A preview only; the Keep note is sent separately.";
    public const string EmptyLine = "Nothing to buy this week.";
    public const string StaplesHeading = "STAPLES · TICK WHAT WE NEED MORE OF";
    public const string NewStaplePrompt = "Something we always keep that isn’t listed? Write it on a line.";
    public const string OverWord = "OVER BUDGET";
    public const string OverAdvice = "Say no to a dinner to bring it down.";

    private const float Top = 78f;
    private const float Limit = 556f;
    private const float BlockGap = 10f;
    private const float HeadingPitch = 17f;
    private const float TickHeadingPitch = 22f;
    private const float LinePitch = 15f;
    private const float AisleGap = 4f;
    private const float FramePad = 8f;
    private const int BodyChars = 62;
    private const int SmallChars = 66;
    private const int FrameChars = 56;

    private const int Columns = 3;
    private const float ColumnPitch = 124f;
    private const float NameOffset = 26f;
    private const float NameWidth = 86f;
    private const int NameChars = 17;
    private const float NameLinePitch = 12.5f;
    private const float MinRow = 27f;

    private const float NewLabelWidth = 66f;
    private const float NewPromptToBox = 23f;
    private const float PromptPitch = 14f;
    private const float NewPitch = 32f;

    /// <summary>How many pages the groceries take, whatever the sheet's own length says.</summary>
    public static int Count(MealSheetModel model) => Lay(model, model.Pages, 0).Count;

    public static IReadOnlyList<InkPageLayout> Build(MealSheetModel model, int firstSheet) => Lay(model, model.Pages, firstSheet);

    /// <summary>Every standing staple in the order it is printed: by store section, then by name, numbered within its section.</summary>
    public static IReadOnlyList<MealSheetStaple> Staples(MealSheetGroceries groceries)
    {
        ArgumentNullException.ThrowIfNull(groceries);
        var staples = new List<MealSheetStaple>();
        foreach (var aisle in Aisles.All)
        {
            var slot = 0;
            foreach (var item in groceries.Pantry.Standing.Where(s => s.Aisle == aisle))
            {
                staples.Add(new MealSheetStaple(MealSheetIds.StapleNeed(aisle, ++slot), PageText.Capitalise(item.Name), aisle));
            }
        }

        return staples;
    }

    /// <summary>The id and the words of every box a pen answers on these pages: the staples, then the blank lines for new ones.</summary>
    public static IReadOnlyList<(string Id, string Subject)> Slots(MealSheetGroceries groceries) =>
        Staples(groceries).Select(s => (s.Id, s.Name))
            .Concat(Enumerable.Range(1, MealSheetIds.NewStapleLines).Select(n => (MealSheetIds.NewStaple(n), $"A new staple, on line {n.ToString(CultureInfo.InvariantCulture)}")))
            .ToList();

    private static List<InkPageLayout> Lay(MealSheetModel model, int total, int firstSheet)
    {
        var groceries = model.Groceries ?? throw new ArgumentException("The sheet carries no groceries.", nameof(model));
        var flow = new Flow(model, total, firstSheet);

        Budget(flow, groceries.Budget);
        List(flow, groceries.List);
        StapleGrid(flow, groceries);
        NewStaples(flow);
        return flow.Finish();
    }

    private static void Budget(Flow flow, GroceryBudgetStatus budget)
    {
        switch (budget.State)
        {
            case GroceryBudgetState.Over:
                var words = PageText.Wrap(budget.Sentence + " " + OverAdvice, FrameChars);
                var height = FramePad + HeadingPitch + words.Count * LinePitch + FramePad - 2f;
                flow.Need(height);
                var top = flow.Y;
                var draft = flow.Draft;
                var left = PageDraft.Left;
                var right = PageDraft.RightEdge;
                draft.Rule("budget-flag-top", left, top, PageDraft.FullWidth, 0f);
                draft.Rule("budget-flag-bottom", left, top + height, PageDraft.FullWidth, 0f);
                draft.Rule("budget-flag-left", left, top, 0f, height);
                draft.Rule("budget-flag-right", right, top, 0f, height);
                draft.Line(PrintedRole.Label, left + FramePad, top + FramePad + 9f, PageDraft.FullWidth - 2f * FramePad, OverWord, name: "budget-label");
                var baseline = top + FramePad + HeadingPitch + 7f;
                var index = 0;
                foreach (var line in words)
                {
                    draft.Line(PrintedRole.Body, left + FramePad, baseline, PageDraft.FullWidth - 2f * FramePad, line, name: "budget-line-" + ++index);
                    baseline += LinePitch;
                }

                flow.Advance(height + BlockGap);
                break;
            case GroceryBudgetState.Within:
                var plain = PageText.Wrap(budget.Sentence, SmallChars);
                flow.Need(plain.Count * LinePitch);
                var plainIndex = 0;
                foreach (var line in plain)
                {
                    flow.Draft.Line(PrintedRole.Small, PageDraft.Left, flow.Y + 12f, PageDraft.FullWidth, line, name: "budget-line-" + ++plainIndex);
                    flow.Advance(LinePitch);
                }

                flow.Advance(BlockGap - 2f);
                break;
        }
    }

    private static void List(Flow flow, GroceryList list)
    {
        flow.Need(HeadingPitch + LinePitch);
        flow.Draft.Line(PrintedRole.Label, PageDraft.Left, flow.Y + 10f, PageDraft.FullWidth, ListHeading, name: "list-heading");
        flow.Advance(HeadingPitch);
        foreach (var line in PageText.Wrap(PreviewLine, SmallChars))
        {
            flow.Need(LinePitch);
            flow.Draft.Line(PrintedRole.Small, PageDraft.Left, flow.Y + 11f, PageDraft.FullWidth, line, name: "list-note");
            flow.Advance(LinePitch);
        }

        flow.Advance(AisleGap);
        if (list.IsEmpty)
        {
            flow.Need(LinePitch);
            flow.Draft.Line(PrintedRole.Body, PageDraft.Left, flow.Y + 12f, PageDraft.FullWidth, EmptyLine, name: "list-empty");
            flow.Advance(LinePitch + BlockGap);
            return;
        }

        foreach (var section in list.Sections)
        {
            var word = Aisles.Words.Word(section.Aisle);
            var label = Aisles.Label(section.Aisle).ToUpperInvariant();
            var text = string.Join(", ", section.Lines.Select(l => string.IsNullOrEmpty(l.Quantity) ? l.Name : $"{l.Name} ({l.Quantity})"));
            var lines = PageText.Wrap(text, BodyChars);

            flow.Need(HeadingPitch + LinePitch, label + " · CONTINUED");
            flow.Draft.Line(PrintedRole.Label, PageDraft.Left, flow.Y + 10f, PageDraft.FullWidth, label, name: word + "-aisle");
            flow.Advance(HeadingPitch);
            var number = 0;
            foreach (var line in lines)
            {
                flow.Need(LinePitch, label + " · CONTINUED");
                flow.Draft.Line(PrintedRole.Body, PageDraft.Left, flow.Y + 12.2f, PageDraft.FullWidth, line, name: $"list-{word}-{++number}");
                flow.Advance(LinePitch);
            }

            flow.Advance(AisleGap);
        }

        flow.Advance(BlockGap - AisleGap);
    }

    private static void StapleGrid(Flow flow, MealSheetGroceries groceries)
    {
        var staples = Staples(groceries);
        if (staples.Count == 0)
        {
            return;
        }

        const string Continued = "STAPLES · CONTINUED";
        for (var start = 0; start < staples.Count; start += Columns)
        {
            var row = staples.Skip(start).Take(Columns).Select(s => (Staple: s, Lines: PageText.Wrap(s.Name, NameChars))).ToList();
            var tallest = row.Max(r => r.Lines.Count);
            var height = Math.Max(MinRow, 12f + NameLinePitch * (tallest - 1) + 4.2f + 9f);
            var heading = start == 0 ? TickHeadingPitch : 0f;
            flow.Need(heading + height, start == 0 ? null : Continued, TickHeadingPitch);
            if (start == 0)
            {
                flow.Draft.Line(PrintedRole.Label, PageDraft.Left, flow.Y + 10f, PageDraft.FullWidth, StaplesHeading, name: "staples-heading");
                flow.Advance(TickHeadingPitch);
            }

            for (var column = 0; column < row.Count; column++)
            {
                var (staple, lines) = row[column];
                var left = PageDraft.Left + column * ColumnPitch;
                var tick = InkTargets.TickRows(staple.Id, left, flow.Y, [$"{staple.Name} is needed"])[0] with { Id = staple.Id };
                flow.Draft.Target(tick);
                var baseline = flow.Y + 12f;
                var number = 0;
                foreach (var line in lines)
                {
                    flow.Draft.Line(PrintedRole.Small, left + NameOffset, baseline, NameWidth, line, owner: staple.Id, name: $"staple-name-{staple.Id}-{++number}");
                    baseline += NameLinePitch;
                }
            }

            flow.Advance(height);
        }

        flow.Advance(BlockGap - 2f);
    }

    private static void NewStaples(Flow flow)
    {
        var promptLines = PageText.Wrap(NewStaplePrompt, SmallChars);
        flow.Need((promptLines.Count - 1) * PromptPitch + NewPromptToBox + (MealSheetIds.NewStapleLines - 1) * NewPitch + InkTargets.WriteLineHeight);
        var number = 0;
        foreach (var line in promptLines)
        {
            flow.Draft.Line(PrintedRole.Small, PageDraft.Left, flow.Y + 10f, PageDraft.FullWidth, line, name: "new-prompt-" + ++number);
            flow.Advance(PromptPitch);
        }

        flow.Advance(NewPromptToBox - PromptPitch);
        for (var n = 1; n <= MealSheetIds.NewStapleLines; n++)
        {
            var id = MealSheetIds.NewStaple(n);
            var left = PageDraft.Left + NewLabelWidth + InkTargets.MinimumClearance;
            flow.Draft.Target(InkTargets.WriteLine(id, left, flow.Y, PageDraft.RightEdge - left, "a new staple, written by hand"));
            flow.Draft.Line(PrintedRole.Small, PageDraft.Left, flow.Y + 16f, NewLabelWidth, "New staple", owner: id, name: "new-label-" + n.ToString(CultureInfo.InvariantCulture));
            flow.Advance(NewPitch);
        }
    }

    /// <summary>The pages being laid out and the cursor down the current one.</summary>
    private sealed class Flow
    {
        private readonly MealSheetModel model;
        private readonly int total;
        private readonly int firstSheet;
        private readonly List<PageDraft> drafts = [];

        public Flow(MealSheetModel model, int total, int firstSheet)
        {
            this.model = model with { Pages = total };
            this.total = total;
            this.firstSheet = firstSheet;
            Begin(Title);
        }

        public PageDraft Draft => drafts[^1];

        public float Y { get; private set; }

        public void Advance(float by) => Y += by;

        /// <summary>Makes room for a block of this height, turning the page (and repeating the heading it was under) when it will not fit.</summary>
        public void Need(float height, string? continued = null, float continuedPitch = HeadingPitch)
        {
            if (Y + height <= Limit)
            {
                return;
            }

            Begin(ContinuedTitle);
            if (continued is not null)
            {
                Draft.Line(PrintedRole.Label, PageDraft.Left, Y + 10f, PageDraft.FullWidth, continued, name: "continued-heading");
                Advance(continuedPitch);
            }
        }

        public List<InkPageLayout> Finish()
        {
            var layouts = new List<InkPageLayout>();
            for (var index = 0; index < drafts.Count; index++)
            {
                MealSheetDocument.Footer(drafts[index], model, firstSheet + index);
                layouts.Add(drafts[index].ToLayout());
            }

            return layouts;
        }

        private void Begin(string title)
        {
            var draft = new PageDraft(title);
            draft.Kicker($"{MealSheetDocument.Kind} · {(firstSheet + drafts.Count).ToString(CultureInfo.InvariantCulture)} OF {total.ToString(CultureInfo.InvariantCulture)}");
            drafts.Add(draft);
            Y = Top;
        }
    }
}
