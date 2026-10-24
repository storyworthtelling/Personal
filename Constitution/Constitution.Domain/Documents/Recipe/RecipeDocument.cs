using System.Globalization;
using Constitution.Domain.Time;

namespace Constitution.Domain.Documents;

/// <summary>
/// The recipe document's first page (book p.8): tonight's headcount stated first, the ingredients
/// scaled to it and grouped by when they are used with the quantities right-aligned in mono, the
/// equipment to have out, a line saying what the card was scaled from, and the notes margin (D35).
/// It takes a plain model in and returns laid-out pages for the same renderer the workout uses; the page
/// has no tick or circle to draw, so its one target is the notes margin from <see cref="InkTargets"/>.
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

    /// <summary>The ingredients page has no rating or tick below it, so content may run to the foot of the body.</summary>
    private const float ContentLimit = PageDraft.CoverContentLimit;

    public static IReadOnlyList<InkPageLayout> Build(RecipeDocumentModel model)
    {
        Validate(model);
        return [IngredientsPage(model)];
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

    private static InkPageLayout IngredientsPage(RecipeDocumentModel model)
    {
        var draft = new PageDraft(model.Title);
        draft.Kicker($"RECIPE · {model.SourceLine.ToUpperInvariant()}");
        draft.NotesColumn();
        Footer(draft, model);

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

        foreach (var line in WrapSegments(TimingSegments(model), NoteChars))
        {
            baseline += LineStep;
            draft.Line(PrintedRole.Small, PageDraft.Left, baseline, PageDraft.ColumnWidth, line);
        }

        var tonight = model.Headcount.Total;
        var scaled = model.Ingredients
            .Select(i => (Ingredient: i, Quantity: ServingsScale.Scale(i.Quantity, model.CardServings, tonight)))
            .ToList();

        baseline = Math.Max(FirstGroupBaseline, baseline + GroupLabelGap);
        var count = 0;
        var groupNumber = 0;
        foreach (var group in scaled.GroupBy(s => s.Ingredient.Group.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            groupNumber++;
            draft.Line(PrintedRole.Label, PageDraft.Left, baseline, PageDraft.ColumnWidth, group.Key.ToUpperInvariant(), name: $"group-{groupNumber}");
            baseline += FirstRowGap;

            foreach (var (ingredient, quantity) in group)
            {
                count++;
                baseline = Row(draft, count, baseline, quantity.Text, ingredient.Name.Trim());
                baseline += RowPitch;
            }

            baseline += GroupGap - RowPitch;
        }

        // The loop leaves the baseline one group gap below the last row; the rule sits a step under that row.
        var lastRow = baseline - GroupGap;
        var rule = lastRow + EquipmentRuleGap;
        draft.Rule("equipment-rule", PageDraft.Left, rule, PageDraft.ColumnWidth, 0f);

        var labelAt = rule + EquipmentLabelGap;
        draft.Line(PrintedRole.Label, PageDraft.Left, labelAt, PageDraft.ColumnWidth, "HAVE OUT", name: "equipment-label");
        var have = RecipeEquipment.From(model.StepTexts, model.Ingredients, model.ExtraEquipment);
        var at = labelAt + FirstEquipmentGap - EquipmentPitch;
        var item = 0;
        foreach (var equipment in have)
        {
            item++;
            var lines = PageText.Wrap(equipment, EquipmentChars);
            for (var i = 0; i < lines.Count; i++)
            {
                at += EquipmentPitch;
                var text = i == 0 ? "·  " + lines[i] : lines[i];
                draft.Line(PrintedRole.Body, PageDraft.Left + (i == 0 ? 0f : EquipmentIndent), at, PageDraft.ColumnWidth, text, name: $"equipment-{item}-{i + 1}");
            }
        }

        var notes = NoteLines(model, scaled);
        var noteAt = (have.Count == 0 ? labelAt - FirstEquipmentGap + EquipmentPitch : at) + NoteGap;
        var lastNote = noteAt;
        foreach (var note in notes)
        {
            draft.Line(PrintedRole.Small, PageDraft.Left, lastNote, PageDraft.ColumnWidth, note, name: "note");
            lastNote += NotePitch;
        }

        PageDraft.RequireRoom(lastNote - NotePitch, ContentLimit, $"The ingredients page for {model.Title} ({count} ingredients)");
        return draft.ToLayout();
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

    private static IReadOnlyList<string> TimingSegments(RecipeDocumentModel model)
    {
        var minutes = model.TotalMinutes.ToString(CultureInfo.InvariantCulture);
        var total = model.HandsOnMinutes is { } hands
            ? $"{minutes} minutes, {hands.ToString(CultureInfo.InvariantCulture)} of them hands-on"
            : $"{minutes} minutes";
        return [$"Dinner {TimeFormats.Clock12(model.DinnerAt)}", $"start {TimeFormats.Clock12(model.StartAt)}", total];
    }

    private static IReadOnlyList<string> NoteLines(
        RecipeDocumentModel model, IReadOnlyList<(RecipeIngredient Ingredient, ScaledQuantity Quantity)> scaled)
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

        if (model.KidsDinnerPage is { } page)
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

    private static void Footer(PageDraft draft, RecipeDocumentModel model)
    {
        var date = model.Day.ToString("ddd MMM d", CultureInfo.InvariantCulture);
        draft.Line(PrintedRole.Small, PageDraft.Left, FooterBaseline, 300f, $"Constitution · Recipe · {date} · {model.Cook.Trim()}", name: "footer");
        draft.Line(PrintedRole.Small, PageDraft.RightEdge - 100f, FooterBaseline, 100f, $"1 of {model.Pages.ToString(CultureInfo.InvariantCulture)}", PrintedAlign.Right, name: "page-number");
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

        if (model.Pages < 1 || model.KidsDinnerPage is { } kids && (kids < 2 || kids > model.Pages))
        {
            throw new ArgumentException($"A recipe of {model.Pages} pages cannot send the cook to page {model.KidsDinnerPage}.", nameof(model));
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
