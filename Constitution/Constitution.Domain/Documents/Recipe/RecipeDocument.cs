using System.Globalization;
using Constitution.Domain.Time;

namespace Constitution.Domain.Documents;

/// <summary>
/// The recipe document (book p.8 and p.9). Page 1: tonight's headcount stated first, the ingredients scaled to it and
/// grouped by when they are used with the quantities right-aligned in mono, the equipment to have out, a line saying
/// what the card was scaled from, and the notes margin (D35). Then the steps, each at a clock time worked back from
/// dinner in Cascadia at the Title rung, with asides for the why and the hand-off to the kids' page; then, on nights
/// the children eat differently, their own page. A page that does not hold what it is given flows to a continuation
/// page rather than refusing. It takes a plain model in and returns laid-out pages for the same renderer the workout
/// uses; no recipe page has a tick or circle, so each one's only target is the notes margin from <see cref="InkTargets"/>.
/// </summary>
public static class RecipeDocument
{
    /// <summary>The right edge of the quantity column: quantities end here, so the eye runs down one column.</summary>
    private const float QuantityRight = 96f;

    private const float NameLeft = 108f;
    private const float NameWidth = PageDraft.ColumnRight - NameLeft;

    /// <summary>How many characters of Cascadia fit left of <see cref="QuantityRight"/> in the 60 points from the margin.</summary>
    private const int QuantityChars = 9;

    /// <summary>Ingredient names in the 198 point name column, in the body face (measured against real glyphs by a test).</summary>
    private const int NameChars = 32;

    /// <summary>The semibold headcount line across the 270 point column.</summary>
    private const int HeadcountChars = 46;

    /// <summary>The secondary sans across the column, a little under <see cref="PageText.SmallColumnChars"/> for the wide letters.</summary>
    private const int NoteChars = 52;

    private const int EquipmentChars = 44;

    private const float HeadcountBaseline = 112f;
    private const float LineStep = 16f;
    /// <summary>Where the book puts the first group's label; a wrapped line above it may push it lower, never higher.</summary>
    private const float FirstGroupBaseline = 162f;

    private const float GroupLabelGap = 18f;
    private const float FirstRowGap = 18f;
    private const float RowPitch = 19f;
    private const float WrappedPitch = 14f;
    private const float GroupGap = 23f;
    private const float EquipmentRuleGap = 23f;
    private const float EquipmentLabelGap = 20f;
    private const float FirstEquipmentGap = 18f;
    private const float EquipmentPitch = 17f;
    private const float NoteGap = 31f;
    private const float NotePitch = 14f;
    private const float EquipmentIndent = 12f;
    private const float FooterBaseline = 578f;

    /// <summary>A continuation page starts its rows where the first page of that kind starts its lead line.</summary>
    private const float ContinuedLabelBaseline = 112f;

    /// <summary>The steps page's lead line sits above the notes column, which therefore starts lower (p.8).</summary>
    private const float StepsNotesTop = 124f;

    /// <summary>
    /// The lead line's baseline. The book draws it at 112, which leaves 7.8 points between its descenders and the notes column
    /// at 124, inside the 8 point clearance every target keeps; half a point higher is the nearest place that clears.
    /// </summary>
    private const float LeadBaseline = 111.5f;

    /// <summary>The first rule under the lead line, where the book draws it.</summary>
    private const float FirstRule = 120f;
    private const float StepLeft = 104f;
    private const float StepWidth = PageDraft.ColumnRight - StepLeft;
    private const float TimeWidth = 64f;
    private const float TimeDrop = 22f;
    private const float FirstBodyGap = 18f;
    private const float AsideTail = 13f;
    private const float BodyTail = 16f;
    private const float MinimumStep = 46f;
    private const float KidsRuleGap = 21f;

    /// <summary>A step's words in the 202 point column beside the time, in the body face (measured against real glyphs by a test).</summary>
    private const int StepChars = 36;

    /// <summary>An aside in the same column in the secondary sans.</summary>
    private const int AsideChars = 42;

    /// <summary>The ingredients page has no rating or tick below it, so content may run to the foot of the body.</summary>
    private const float ContentLimit = PageDraft.CoverContentLimit;

    /// <summary>Where the recipe's pages fall: how many there are in all and where the children's dinner is.</summary>
    private readonly record struct PageNumbers(int Total, int? KidsPage);

    /// <summary>The pages of each kind, kept apart so the numbers can be worked out from how many of each there are.</summary>
    private sealed record PageSet(List<InkPageLayout> Ingredients, List<InkPageLayout> Steps, List<InkPageLayout> Kids)
    {
        public IReadOnlyList<InkPageLayout> All => [.. Ingredients, .. Steps, .. Kids];
    }

    public static IReadOnlyList<InkPageLayout> Build(RecipeDocumentModel model)
    {
        Validate(model);

        // The notes on page one and the hand-offs name pages that depend on how many pages come before them, so the
        // document is laid out until the numbers it printed are the numbers it ended up with. Digits do not change a
        // line's length, so this settles on the second pass.
        var numbers = model.Steps is null
            ? new PageNumbers(model.Pages, model.KidsDinnerPage)
            : new PageNumbers(1, model.KidsMeal is null ? null : 2);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var set = Compose(model, numbers);
            var settled = model.Steps is null
                ? new PageNumbers(model.Pages + set.Ingredients.Count - 1, model.KidsDinnerPage + set.Ingredients.Count - 1)
                : new PageNumbers(
                    set.Ingredients.Count + set.Steps.Count + set.Kids.Count + model.TrailingPages,
                    model.KidsMeal is null ? null : set.Ingredients.Count + set.Steps.Count + 1);
            if (settled == numbers)
            {
                return set.All;
            }

            numbers = settled;
        }

        throw new InvalidOperationException($"The pages of '{model.Title}' would not settle on their numbers.");
    }

    private static PageSet Compose(RecipeDocumentModel model, PageNumbers numbers)
    {
        var clock = model.Steps is null
            ? null
            : RecipeSchedule.Plan(model.Steps, model.DinnerAt, model.StartAt, model.NotBefore);
        var startAt = clock?.Rows[0].At ?? model.StartAt;
        IReadOnlyList<string> stepTexts = model.Steps is null ? model.StepTexts : model.Steps.Select(s => s.Text).ToList();

        var ingredients = IngredientPages(model, numbers, startAt, stepTexts);
        var steps = clock is null ? [] : StepPages(model, clock, numbers, ingredients.Count + 1);
        var kids = clock is null || model.KidsMeal is null
            ? []
            : KidsPages(model, model.KidsMeal, numbers, ingredients.Count + steps.Count + 1);
        return new PageSet(ingredients, steps, kids);
    }

    /// <summary>"for 4: 2 adults, 2 children": who is eating, guests included.</summary>
    public static string HeadcountText(RecipeHeadcount headcount)
    {
        var parts = new List<string>();
        AddCount(parts, headcount.Adults, "adult", "adults");
        AddCount(parts, headcount.Children, "child", "children");
        AddCount(parts, headcount.Guests, "guest", "guests");
        return $"for {headcount.Total.ToString(CultureInfo.InvariantCulture)}: {string.Join(", ", parts)}";
    }

    private static void AddCount(List<string> parts, int count, string one, string many)
    {
        if (count > 0)
        {
            parts.Add($"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? one : many)}");
        }
    }

    private static PageDraft StartPage(RecipeDocumentModel model, string title, string kicker, int page, int total, float notesTop = PageDraft.NotesTop)
    {
        var draft = new PageDraft(title);
        draft.Kicker(kicker);
        draft.NotesColumn(notesTop);
        Footer(draft, model, page, total);
        return draft;
    }

    private static List<InkPageLayout> IngredientPages(
        RecipeDocumentModel model, PageNumbers numbers, DateTime startAt, IReadOnlyList<string> stepTexts)
    {
        var layouts = new List<InkPageLayout>();
        var draft = StartPage(model, model.Title, $"RECIPE · {model.SourceLine.ToUpperInvariant()}", 1, numbers.Total);

        var baseline = HeadcountBaseline;
        var headcount = HeadcountText(model.Headcount);
        var lead = string.IsNullOrWhiteSpace(model.WithLine) ? PageText.Capitalise(headcount) : null;
        string[] headSegments = lead is null ? [model.WithLine!.Trim(), headcount] : [lead];
        var first = true;
        foreach (var line in WrapSegments(headSegments, HeadcountChars))
        {
            baseline += first ? 0f : LineStep;
            first = false;
            draft.Line(PrintedRole.Strong, PageDraft.Left, baseline, PageDraft.ColumnWidth, line);
        }

        foreach (var line in WrapSegments(TimingSegments(model, startAt), NoteChars))
        {
            baseline += LineStep;
            draft.Line(PrintedRole.Small, PageDraft.Left, baseline, PageDraft.ColumnWidth, line);
        }

        var tonight = model.Headcount.Total;
        var scaled = model.Ingredients
            .Select(i => (Ingredient: i, Quantity: ServingsScale.Scale(i.Quantity, model.CardServings, tonight)))
            .ToList();

        var labelAt = Math.Max(FirstGroupBaseline, baseline + GroupLabelGap);
        var lastRow = labelAt;
        var count = 0;
        var groupNumber = 0;
        foreach (var group in scaled.GroupBy(s => s.Ingredient.Group.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            groupNumber++;
            var onThisPage = 0;
            var inGroup = 0;
            foreach (var (ingredient, quantity) in group)
            {
                count++;
                var name = ingredient.Name.Trim();
                var fits = quantity.Text.Length <= QuantityChars;
                var words = fits || quantity.Text.Length == 0 ? name : $"{quantity.Text} {name}";
                var depth = WrappedPitch * (PageText.Wrap(words, NameChars).Count - 1);
                var at = onThisPage == 0 ? labelAt + FirstRowGap : lastRow + RowPitch;

                if (at + depth > ContentLimit)
                {
                    layouts.Add(draft.ToLayout());
                    draft = StartPage(model, model.Title, "RECIPE · INGREDIENTS, CONTINUED", layouts.Count + 1, numbers.Total);
                    labelAt = ContinuedLabelBaseline;
                    onThisPage = 0;
                    at = labelAt + FirstRowGap;
                }

                if (onThisPage == 0)
                {
                    var text = group.Key.ToUpperInvariant() + (inGroup > 0 ? " (CONTINUED)" : string.Empty);
                    draft.Line(PrintedRole.Label, PageDraft.Left, labelAt, PageDraft.ColumnWidth, text, name: inGroup > 0 ? $"group-{groupNumber}-continued" : $"group-{groupNumber}");
                }

                lastRow = Row(draft, count, at, quantity.Text, name);
                onThisPage++;
                inGroup++;
            }

            labelAt = lastRow + GroupGap;
        }

        var have = RecipeEquipment.From(stepTexts, model.Ingredients, model.ExtraEquipment);
        var equipmentLines = have.Select(e => PageText.Wrap(e, EquipmentChars)).ToList();
        var notes = NoteLines(model, numbers, scaled);

        if (TailEnd(lastRow, equipmentLines, notes.Count) > ContentLimit)
        {
            layouts.Add(draft.ToLayout());
            draft = StartPage(model, model.Title, "RECIPE · INGREDIENTS, CONTINUED", layouts.Count + 1, numbers.Total);
            lastRow = ContinuedLabelBaseline - EquipmentRuleGap;
        }

        PageDraft.RequireRoom(TailEnd(lastRow, equipmentLines, notes.Count), ContentLimit, $"The equipment list and notes for {model.Title} ({have.Count} items of equipment, {count} ingredients)");
        Tail(draft, lastRow, equipmentLines, notes);
        layouts.Add(draft.ToLayout());
        return layouts;
    }

    /// <summary>Where the last note's baseline falls if the tail starts under a row ending at <paramref name="lastRow"/>.</summary>
    private static float TailEnd(float lastRow, IReadOnlyList<IReadOnlyList<string>> equipment, int noteCount)
    {
        var labelAt = lastRow + EquipmentRuleGap + EquipmentLabelGap;
        var lines = equipment.Sum(e => e.Count);
        var at = lines == 0 ? labelAt - FirstEquipmentGap + EquipmentPitch : labelAt + FirstEquipmentGap + (EquipmentPitch * (lines - 1));
        return at + NoteGap + (NotePitch * (noteCount - 1));
    }

    /// <summary>The rule under the ingredients, what to have out, and the notes about the scaling.</summary>
    private static void Tail(PageDraft draft, float lastRow, IReadOnlyList<IReadOnlyList<string>> equipment, IReadOnlyList<string> notes)
    {
        var rule = lastRow + EquipmentRuleGap;
        draft.Rule("equipment-rule", PageDraft.Left, rule, PageDraft.ColumnWidth, 0f);

        var labelAt = rule + EquipmentLabelGap;
        draft.Line(PrintedRole.Label, PageDraft.Left, labelAt, PageDraft.ColumnWidth, "HAVE OUT", name: "equipment-label");
        var at = labelAt + FirstEquipmentGap - EquipmentPitch;
        var item = 0;
        foreach (var lines in equipment)
        {
            item++;
            for (var i = 0; i < lines.Count; i++)
            {
                at += EquipmentPitch;
                var text = i == 0 ? "·  " + lines[i] : lines[i];
                draft.Line(PrintedRole.Body, PageDraft.Left + (i == 0 ? 0f : EquipmentIndent), at, PageDraft.ColumnWidth, text, name: $"equipment-{item}-{i + 1}");
            }
        }

        var noteAt = (equipment.Count == 0 ? labelAt - FirstEquipmentGap + EquipmentPitch : at) + NoteGap;
        foreach (var note in notes)
        {
            draft.Line(PrintedRole.Small, PageDraft.Left, noteAt, PageDraft.ColumnWidth, note, name: "note");
            noteAt += NotePitch;
        }
    }

    /// <summary>"5:15": the hour and minutes on a 12-hour face, the am or pm left to the page's own lead line (the book's way).</summary>
    private static string StepFace(DateTime at) => TimeFormats.Clock12(at)[..^3];

    /// <summary>One printed row of the clock: its time, its words and the asides under them.</summary>
    private sealed record StepRow(string Time, IReadOnlyList<string> Body, IReadOnlyList<string> Asides)
    {
        public int Lines => Body.Count + Asides.Count;

        /// <summary>From this row's rule to the next one: the book's pitch, never less than a one-line step takes.</summary>
        public float Height => Math.Max(MinimumStep, FirstBodyGap + (LineStep * (Lines - 1)) + (Asides.Count > 0 ? AsideTail : BodyTail));

        /// <summary>The lowest baseline the row draws, counted from its rule.</summary>
        public float Reach => Math.Max(TimeDrop, FirstBodyGap + (LineStep * (Lines - 1)));
    }

    private static StepRow ToRow(ClockedRow row, string dinnerLine, RecipeKidsMeal? kids, PageNumbers numbers)
    {
        if (row.Step is not { } step)
        {
            return new StepRow(StepFace(row.At), PageText.Wrap(dinnerLine, StepChars), []);
        }

        var asides = new List<string>();
        if (!string.IsNullOrWhiteSpace(step.Aside))
        {
            asides.AddRange(PageText.Wrap(step.Aside.Trim(), AsideChars));
        }

        if (!string.IsNullOrWhiteSpace(step.CalendarEvent))
        {
            asides.AddRange(PageText.Wrap($"On the calendar: “{step.CalendarEvent.Trim()}.”", AsideChars));
        }

        if (step.KidsHandOff && kids is not null)
        {
            asides.AddRange(PageText.Wrap($"Kids’ {kids.ShortName.Trim()}: page {(numbers.KidsPage ?? 0).ToString(CultureInfo.InvariantCulture)}.", AsideChars));
        }

        return new StepRow(StepFace(row.At), PageText.Wrap(step.Text.Trim(), StepChars), asides);
    }

    /// <summary>
    /// Deals the rows onto pages: each page takes rows while the next one still fits, the first page from
    /// <paramref name="firstRule"/> and the rest from the top. A page never ends on the dinner alone, which would be a page of one word.
    /// </summary>
    private static List<List<StepRow>> Deal(IReadOnlyList<StepRow> rows, float firstRule, float laterRule)
    {
        var pages = new List<List<StepRow>> { new() };
        var rule = firstRule;
        foreach (var row in rows)
        {
            if (rule + row.Reach > ContentLimit && (pages[^1].Count > 0 || (pages.Count == 1 && firstRule > laterRule)))
            {
                pages.Add([]);
                rule = laterRule;
            }

            pages[^1].Add(row);
            rule += row.Height;
        }

        if (pages.Count > 1 && pages[^1].Count == 1 && pages[^2].Count > 1)
        {
            var moved = pages[^2][^1];
            pages[^2].RemoveAt(pages[^2].Count - 1);
            pages[^1].Insert(0, moved);
        }

        return pages;
    }

    private static void DrawRows(PageDraft draft, IReadOnlyList<StepRow> rows, float firstRule, ref int number)
    {
        var rule = firstRule;
        foreach (var row in rows)
        {
            number++;
            draft.Rule($"step-rule-{number}", PageDraft.Left, rule, PageDraft.ColumnWidth, 0f);
            draft.Line(PrintedRole.Time, PageDraft.Left, rule + TimeDrop, TimeWidth, row.Time, name: $"step-{number}-time");

            var at = rule + FirstBodyGap;
            var line = 0;
            foreach (var text in row.Body)
            {
                draft.Line(PrintedRole.Body, StepLeft, at, StepWidth, text, name: $"step-{number}-{++line}");
                at += LineStep;
            }

            var aside = 0;
            foreach (var text in row.Asides)
            {
                draft.Line(PrintedRole.Small, StepLeft, at, StepWidth, text, name: $"step-{number}-aside-{++aside}");
                at += LineStep;
            }

            rule += row.Height;
        }
    }

    private static List<InkPageLayout> StepPages(RecipeDocumentModel model, RecipeClock clock, PageNumbers numbers, int firstPage)
    {
        var slow = model.Steps!.Any(s => s.Morning);
        var title = slow ? model.Title : "Steps";
        var kicker = slow
            ? "RECIPE · SLOW COOKER · ARRIVES BEFORE THE MORNING BLOCK"
            : $"RECIPE · TIMED BACK FROM DINNER AT {StepFace(model.DinnerAt)}";
        var leadWords = slow
            ? "Started in the morning, ready at dinner. Morning steps first."
            : clock.SqueezedTo is { } now
                ? $"Tight tonight: the first steps start together at {StepFace(now)}."
                : "Clock times tonight, from dinner back. Late? Every step moves together.";

        var rows = clock.Rows.Select(r => ToRow(r, "Dinner.", model.KidsMeal, numbers)).ToList();
        var firstRule = FirstRule;
        var dealt = Deal(rows, firstRule, firstRule);

        var layouts = new List<InkPageLayout>();
        var number = 0;
        for (var i = 0; i < dealt.Count; i++)
        {
            var page = firstPage + i;
            var draft = StartPage(model, title, i == 0 ? kicker : kicker + " · CONTINUED", page, numbers.Total, StepsNotesTop);
            var lead = i == 0 ? leadWords : $"Continued from page {(page - 1).ToString(CultureInfo.InvariantCulture)}.";
            draft.Line(PrintedRole.Small, PageDraft.Left, LeadBaseline, PageDraft.FullWidth, lead, name: "step-lead");
            DrawRows(draft, dealt[i], firstRule, ref number);
            layouts.Add(draft.ToLayout());
        }

        return layouts;
    }

    private static List<InkPageLayout> KidsPages(RecipeDocumentModel model, RecipeKidsMeal meal, PageNumbers numbers, int firstPage)
    {
        const string kicker = "RECIPE · THE CHILDREN’S DINNER TONIGHT";
        var clock = RecipeSchedule.Plan(meal.Steps, model.DinnerAt, model.StartAt, model.NotBefore);
        var rows = clock.Rows.Select(r => ToRow(r, meal.DinnerLine.Trim(), null, numbers)).ToList();

        var draft = StartPage(model, meal.Title.Trim(), kicker, firstPage, numbers.Total);
        var baseline = HeadcountBaseline;
        var first = true;
        foreach (var line in PageText.Wrap("For " + NameList(meal.Children), HeadcountChars))
        {
            baseline += first ? 0f : LineStep;
            first = false;
            draft.Line(PrintedRole.Strong, PageDraft.Left, baseline, PageDraft.ColumnWidth, line);
        }

        foreach (var line in PageText.Wrap(meal.WhyLine.Trim(), NoteChars))
        {
            baseline += LineStep;
            draft.Line(PrintedRole.Small, PageDraft.Left, baseline, PageDraft.ColumnWidth, line);
        }

        var labelAt = Math.Max(FirstGroupBaseline, baseline + GroupLabelGap);
        draft.Line(PrintedRole.Label, PageDraft.Left, labelAt, PageDraft.ColumnWidth, "INGREDIENTS", name: "group-1");
        var at = labelAt + FirstRowGap;
        var lastRow = at;
        var count = 0;
        foreach (var ingredient in meal.Ingredients)
        {
            count++;
            lastRow = Row(draft, count, count == 1 ? at : lastRow + RowPitch, ingredient.Quantity.Trim(), ingredient.Name.Trim());
        }

        PageDraft.RequireRoom(lastRow, ContentLimit, $"The children's ingredients for {meal.Title} ({count} ingredients)");

        var firstRule = lastRow + KidsRuleGap;
        var laterRule = FirstRule;
        var dealt = Deal(rows, firstRule, laterRule);
        var layouts = new List<InkPageLayout>();
        var number = 0;
        DrawRows(draft, dealt[0], firstRule, ref number);
        layouts.Add(draft.ToLayout());

        for (var i = 1; i < dealt.Count; i++)
        {
            var page = firstPage + i;
            var more = StartPage(model, meal.Title.Trim(), kicker + " · CONTINUED", page, numbers.Total, StepsNotesTop);
            more.Line(PrintedRole.Small, PageDraft.Left, LeadBaseline, PageDraft.FullWidth, $"Continued from page {(page - 1).ToString(CultureInfo.InvariantCulture)}.", name: "step-lead");
            DrawRows(more, dealt[i], laterRule, ref number);
            layouts.Add(more.ToLayout());
        }

        return layouts;
    }

    /// <summary>"Robin", "Robin and Juniper", "Robin, Juniper and Lee".</summary>
    private static string NameList(IReadOnlyList<string> names)
    {
        var clean = names.Select(n => n.Trim()).ToList();
        return clean.Count == 1 ? clean[0] : string.Join(", ", clean.Take(clean.Count - 1)) + " and " + clean[^1];
    }

    /// <summary>One ingredient: its quantity right-aligned in mono, its name beside it, wrapping onto lines of their own.</summary>
    private static float Row(PageDraft draft, int number, float baseline, string quantity, string name)
    {
        var fits = quantity.Length <= QuantityChars;
        var words = fits || quantity.Length == 0 ? name : $"{quantity} {name}";
        if (fits && quantity.Length > 0)
        {
            draft.Line(PrintedRole.Mono, PageDraft.Left, baseline, QuantityRight - PageDraft.Left, quantity, PrintedAlign.Right, name: $"quantity-{number}");
        }

        var lines = PageText.Wrap(words, NameChars);
        for (var i = 0; i < lines.Count; i++)
        {
            draft.Line(PrintedRole.Body, NameLeft, baseline + (WrappedPitch * i), NameWidth, lines[i], name: $"ingredient-{number}-{i + 1}");
        }

        return baseline + (WrappedPitch * (lines.Count - 1));
    }

    private static IReadOnlyList<string> TimingSegments(RecipeDocumentModel model, DateTime startAt)
    {
        var minutes = model.TotalMinutes.ToString(CultureInfo.InvariantCulture);
        var total = model.HandsOnMinutes is { } hands
            ? $"{minutes} minutes, {hands.ToString(CultureInfo.InvariantCulture)} of them hands-on"
            : $"{minutes} minutes";
        return [$"Dinner {TimeFormats.Clock12(model.DinnerAt)}", $"start {TimeFormats.Clock12(startAt)}", total];
    }

    private static IReadOnlyList<string> NoteLines(
        RecipeDocumentModel model, PageNumbers numbers, IReadOnlyList<(RecipeIngredient Ingredient, ScaledQuantity Quantity)> scaled)
    {
        var lines = new List<string>();
        var card = model.CardServings.ToString(CultureInfo.InvariantCulture);
        if (model.CardServings == model.Headcount.Total)
        {
            lines.Add($"Quantities as the card has them: {card} servings.");
        }
        else
        {
            lines.Add($"Scaled from the card’s {card} servings.");
            var left = scaled.Where(s => s.Quantity.Kind == ScaleKind.NotScaled).Select(s => ShortName(s.Ingredient.Name)).ToList();
            if (left.Count > 0)
            {
                lines.AddRange(PageText.Wrap($"Not scaled, as the card has them: {string.Join(", ", left)}.", NoteChars));
            }
        }

        if (numbers.KidsPage is { } page)
        {
            lines.Add($"The children’s dinner is on page {page.ToString(CultureInfo.InvariantCulture)}.");
        }

        return lines;
    }

    /// <summary>"salt, divided" becomes "salt": the words before the first comma or bracket.</summary>
    private static string ShortName(string name)
    {
        var end = name.IndexOfAny([',', '(']);
        return (end > 0 ? name[..end] : name).Trim();
    }

    private static void Footer(PageDraft draft, RecipeDocumentModel model, int page, int total)
    {
        var date = model.Day.ToString("ddd MMM d", CultureInfo.InvariantCulture);
        draft.Line(PrintedRole.Small, PageDraft.Left, FooterBaseline, 300f, $"Constitution · Recipe · {date} · {model.Cook.Trim()}", name: "footer");
        draft.Line(PrintedRole.Small, PageDraft.RightEdge - 100f, FooterBaseline, 100f, $"{page.ToString(CultureInfo.InvariantCulture)} of {total.ToString(CultureInfo.InvariantCulture)}", PrintedAlign.Right, name: "page-number");
    }

    /// <summary>Segments joined by " · " while they fit; one that is too long alone wraps on its words.</summary>
    internal static IReadOnlyList<string> WrapSegments(IReadOnlyList<string> segments, int maxChars)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var segment in segments)
        {
            if (current.Length == 0)
            {
                current = segment;
            }
            else if (current.Length + 3 + segment.Length <= maxChars)
            {
                current += " · " + segment;
            }
            else
            {
                lines.AddRange(PageText.Wrap(current, maxChars));
                current = segment;
            }
        }

        lines.AddRange(PageText.Wrap(current, maxChars));
        return lines;
    }

    private static void Validate(RecipeDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Require(model.Title, "title", "the recipe");
        Require(model.SourceLine, "source line", $"'{model.Title}'");
        Require(model.Cook, "cook", $"'{model.Title}'");
        if (model.CardServings < 1)
        {
            throw new ArgumentException($"'{model.Title}' says the card makes {model.CardServings} servings; a card makes at least one.", nameof(model));
        }

        var h = model.Headcount;
        if (h.Adults < 0 || h.Children < 0 || h.Guests < 0 || h.Total < 1)
        {
            throw new ArgumentException($"A headcount of {h.Adults} adults, {h.Children} children and {h.Guests} guests is nobody to cook for.", nameof(model));
        }

        if (model.Ingredients.Count == 0)
        {
            throw new ArgumentException($"'{model.Title}' has no ingredients to list.", nameof(model));
        }

        foreach (var ingredient in model.Ingredients)
        {
            Require(ingredient.Name, "name", "an ingredient");
            Require(ingredient.Group, "group heading", $"'{ingredient.Name}'");
        }

        if (model.TotalMinutes < 1 || model.HandsOnMinutes is < 1 || model.HandsOnMinutes > model.TotalMinutes)
        {
            throw new ArgumentException($"'{model.Title}' takes {model.TotalMinutes} minutes with {model.HandsOnMinutes} hands-on; hands-on time is within the total.", nameof(model));
        }

        if (model.StartAt > model.DinnerAt)
        {
            throw new ArgumentException($"'{model.Title}' starts after dinner is on the table.", nameof(model));
        }

        if (model.Steps is null)
        {
            if (model.KidsMeal is not null)
            {
                throw new ArgumentException($"'{model.Title}' has a children's dinner but no timed steps to hand the cook over from.", nameof(model));
            }

            if (model.Pages < 1 || model.KidsDinnerPage is { } kids && (kids < 2 || kids > model.Pages))
            {
                throw new ArgumentException($"A recipe of {model.Pages} pages cannot send the cook to page {model.KidsDinnerPage}.", nameof(model));
            }

            return;
        }

        if (model.TrailingPages < 0)
        {
            throw new ArgumentException($"'{model.Title}' cannot have {model.TrailingPages} pages after it.", nameof(model));
        }

        if (model.Steps.Count == 0)
        {
            throw new ArgumentException($"'{model.Title}' has no steps to put on the clock.", nameof(model));
        }

        if (model.KidsMeal is null && model.Steps.FirstOrDefault(s => s.KidsHandOff) is { } stranded)
        {
            throw new ArgumentException($"The step '{stranded.Text}' sends the cook to the children's page but there is no children's dinner tonight.", nameof(model));
        }

        if (model.KidsMeal is { } meal)
        {
            ValidateKids(meal);
        }
    }

    private static void ValidateKids(RecipeKidsMeal meal)
    {
        Require(meal.Title, "title", "the children's dinner");
        Require(meal.ShortName, "short name", $"'{meal.Title}'");
        Require(meal.WhyLine, "line saying why tonight is different", $"'{meal.Title}'");
        Require(meal.DinnerLine, "final line", $"'{meal.Title}'");
        if (meal.Children.Count == 0 || meal.Children.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"'{meal.Title}' needs the names of the children it is for.", nameof(meal));
        }

        if (meal.Ingredients.Count == 0)
        {
            throw new ArgumentException($"'{meal.Title}' has no ingredients to list.", nameof(meal));
        }

        foreach (var ingredient in meal.Ingredients)
        {
            Require(ingredient.Name, "name", "a children's ingredient");
        }

        if (meal.Steps.Count == 0)
        {
            throw new ArgumentException($"'{meal.Title}' has no steps to put on the clock.", nameof(meal));
        }

        if (meal.Steps.Any(s => s.Morning || s.KidsHandOff))
        {
            throw new ArgumentException($"'{meal.Title}' is cooked in the evening beside the main dish; its steps have no morning part and no hand-off to itself.", nameof(meal));
        }
    }

    private static void Require(string? value, string what, string owner)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{owner} needs a {what} to print.");
        }
    }
}
