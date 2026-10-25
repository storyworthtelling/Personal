using System.Globalization;
using Constitution.Domain.Meals;
using Constitution.Domain.Time;

namespace Constitution.Domain.Documents;

/// <summary>
/// Page 1 of the weekly meal sheet (book p.7, treatment B): a row per evening in the planning week's order (Sunday to Saturday), each with a YES box under which "YES" is
/// written, the day, the dish, who cooks and when, any outlined-word marker, and the shaded "No, because" field at the
/// other end of the row. Every box comes from <see cref="InkTargets"/> and every id from <see cref="MealSheetIds"/>, so
/// the ink reader reads the instances the painter drew. Silence approves at <see cref="MealSheetModel.Deadline"/>;
/// the page says so in words.
/// </summary>
public static class MealSheetDocument
{
    public const string Kind = "WEEKLY MEAL SHEET";
    public const string TickLine = "Tick YES to approve, or write after NO, BECAUSE.";
    public const string YesWord = "YES";
    public const string NoBecauseWord = "No, because";
    public const string SlowCookerWord = "SLOW COOKER";
    public const string NewRecipeWord = "NEW RECIPE";
    public const string EatingOutWord = "EATING OUT";

    /// <summary>The first row's top hairline; the rows run 58 points apart below it.</summary>
    private const float FirstRule = 156f;

    private const float RowPitch = 58f;

    /// <summary>The field is 40 tall and 9 points in from each hairline; the YES box is level with its middle.</summary>
    private const float FieldInset = 9f;

    private const float YesBoxInset = 20f;
    private const float YesLabelBaseline = 57f;
    private const float DayLeft = 62f;
    private const float DayWidth = 28f;
    private const float DishLeft = 94f;

    /// <summary>The reason field is 128 wide; its label stands in the 64 points to its left, with a marker above it.</summary>
    private const float ReasonWidth = 128f;

    private const float LabelColumnWidth = 64f;
    private const float DishGap = 6f;
    private const float LabelBaseline = 36f;
    private const float MarkerTop = 8f;
    private const float MarkerHeight = 14f;
    private const float MarkerTextBaseline = 19f;
    private const float MarkerPad = 5f;

    /// <summary>The semibold label runs about 5.6 points a character; a marker's box is its word plus the padding both sides.</summary>
    private const float MarkerCharWidth = 5.6f;

    private const float VoiceFirstBaseline = 92f;
    private const float VoicePitch = 20f;
    private const int VoiceMaxLinesHere = 2;

    /// <summary>The voiced line runs the full 373 point measure here, not the 270 column: the book breaks it at about 46 characters.</summary>
    private const int VoiceLineChars = 46;

    private const int BodyLineChars = 66;
    private const float TickLineBaseline = 134f;
    private const float DeadlineBaseline = 148f;

    /// <summary>The semibold dish name in the dish column; the secondary sans beside it.</summary>
    private const int DishChars = 18;

    private const int SmallChars = 23;
    private const int MaxRowLines = 4;
    private const float LooseLinePitch = 14f;
    private const float TightLinePitch = 12.5f;

    public static IReadOnlyList<InkPageLayout> Build(MealSheetModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.Week.IsPlanned)
        {
            throw new ArgumentException("A week that was not planned has no dinners to print: " + model.Week.StopReason, nameof(model));
        }

        var draft = new PageDraft("Dinners");
        draft.Kicker($"{Kind} · 1 OF {model.Pages.ToString(CultureInfo.InvariantCulture)}");
        Header(draft, model);

        for (var index = 0; index < model.Week.Dinners.Count; index++)
        {
            Row(draft, model, model.Week.Dinners[index], FirstRule + index * RowPitch);
        }

        Footer(draft, model);
        return [draft.ToLayout()];
    }

    /// <summary>"Sunday morning": the day and which part of it, as the book words the deadline.</summary>
    public static string DeadlineWords(DateTimeOffset deadline)
    {
        var moment = deadline.DateTime;
        var part = moment.Hour < 12 ? "morning" : moment.Hour < 17 ? "afternoon" : "evening";
        return moment.ToString("dddd", CultureInfo.InvariantCulture) + " " + part;
    }

    private static void Header(PageDraft draft, MealSheetModel model)
    {
        var lines = string.IsNullOrWhiteSpace(model.Voice)
            ? []
            : PageText.Wrap(model.Voice.Trim(), model.VoiceIsOwners ? VoiceLineChars : BodyLineChars);
        if (lines.Count is > 0 and <= VoiceMaxLinesHere)
        {
            var baseline = VoiceFirstBaseline;
            foreach (var line in lines)
            {
                draft.Line(model.VoiceIsOwners ? PrintedRole.Voice : PrintedRole.Body, PageDraft.Left, baseline, PageDraft.FullWidth, line, name: "voice-" + (int)((baseline - VoiceFirstBaseline) / VoicePitch + 1));
                baseline += VoicePitch;
            }
        }

        draft.Line(PrintedRole.Small, PageDraft.Left, TickLineBaseline, PageDraft.FullWidth, TickLine, name: "tick-line");
        draft.Line(
            PrintedRole.Small, PageDraft.Left, DeadlineBaseline, PageDraft.FullWidth,
            $"Anything untouched is approved {DeadlineWords(model.Deadline)}.", name: "deadline-line");
    }

    private static void Footer(PageDraft draft, MealSheetModel model)
    {
        var weekOf = model.Week.WeekStart.ToDateTime(TimeOnly.MinValue).ToString("MMM d", CultureInfo.InvariantCulture);
        draft.Line(PrintedRole.Small, PageDraft.Left, 578f, 300f, $"Constitution · Meals · week of {weekOf} · both tablets", name: "footer");
        draft.Line(
            PrintedRole.Small, PageDraft.RightEdge - 100f, 578f, 100f,
            $"1 of {model.Pages.ToString(CultureInfo.InvariantCulture)}", PrintedAlign.Right, name: "page-number");
    }

    private static void Row(PageDraft draft, MealSheetModel model, DinnerChoice dinner, float top)
    {
        var weekday = dinner.Day.DayOfWeek;
        var name = weekday.ToString().ToLowerInvariant();
        var facts = Facts(model, dinner);
        var lines = Wrap(facts, dinner);

        draft.Rule($"{name}-rule", PageDraft.Left, top, PageDraft.FullWidth, 0f);

        var yes = InkTargets.TickRows("dinner", PageDraft.Left, top + YesBoxInset, [$"yes, {facts.Title} is fine"])[0]
            with { Id = MealSheetIds.DinnerYes(weekday) };
        var reason = InkTargets.NoBecause(MealSheetIds.DinnerReason(weekday), yes, PageDraft.RightEdge, ReasonWidth);
        draft.Target(yes);
        draft.Target(reason);

        draft.Line(PrintedRole.Label, yes.Bounds.CentreX - 15f, top + YesLabelBaseline, 30f, YesWord, PrintedAlign.Centre, yes.Id, $"{name}-yes-label");
        draft.Line(
            PrintedRole.Label, reason.Bounds.Left - InkTargets.MinimumClearance - LabelColumnWidth, top + LabelBaseline, LabelColumnWidth,
            NoBecauseWord, PrintedAlign.Right, reason.Id, $"{name}-no-label");

        var pitch = lines.Count <= 3 ? LooseLinePitch : TightLinePitch;
        var baseline = top + (lines.Count <= 3 ? 16f : 14.5f);
        var width = ZoneRight(reason) - DishLeft;
        draft.Line(PrintedRole.Mono, DayLeft, baseline, DayWidth, weekday.ToString()[..3], name: $"{name}-day");
        foreach (var (role, text) in lines)
        {
            draft.Line(role, DishLeft, baseline, width, text, name: $"{name}-line");
            baseline += pitch;
        }

        if (facts.Marker is { } marker)
        {
            Marker(draft, name, marker, reason, top);
        }
    }

    private static float ZoneRight(InkTarget reason) => reason.Bounds.Left - InkTargets.MinimumClearance - LabelColumnWidth - DishGap;

    private static void Marker(PageDraft draft, string name, string word, InkTarget reason, float top)
    {
        var right = reason.Bounds.Left - InkTargets.MinimumClearance;
        var width = word.Length * MarkerCharWidth + 2f * MarkerPad;
        var left = right - width;
        var edge = top + MarkerTop;

        draft.Rule($"{name}-marker-top", left, edge, width, 0f);
        draft.Rule($"{name}-marker-bottom", left, edge + MarkerHeight, width, 0f);
        draft.Rule($"{name}-marker-left", left, edge, 0f, MarkerHeight);
        draft.Rule($"{name}-marker-right", right, edge, 0f, MarkerHeight);
        draft.Line(PrintedRole.Label, left + MarkerPad, top + MarkerTextBaseline, width - 2f * MarkerPad, word, name: $"{name}-marker");
    }

    private sealed record RowFacts(string Title, string? Cook, string? Kids, string? Marker, string? Reason);

    private static RowFacts Facts(MealSheetModel model, DinnerChoice dinner)
    {
        var day = dinner.Day.DayOfWeek;
        switch (dinner.Kind)
        {
            case DinnerKind.Cooked:
                if (dinner.Recipe is not { } card || !model.Dishes.TryGetValue(card, out var dish))
                {
                    throw new ArgumentException($"{day}'s dinner is a card the sheet has no dish for ({dinner.Recipe}); say what it is called.", nameof(model));
                }

                var start = dinner.CookStart!.Value.DateTime;
                var cook = dish.Marker == MealMarker.SlowCooker
                    ? $"{dinner.Cook} starts it {TimeFormats.Clock12(start)}"
                    : $"{dinner.Cook} cooks from {TimeFormats.Clock12(start)}";
                var kids = dinner.ChildrenEatDifferently && !string.IsNullOrWhiteSpace(dish.KidsMeal) ? $"Kids: {dish.KidsMeal!.Trim()}" : null;
                var marker = dish.Marker switch
                {
                    MealMarker.SlowCooker => SlowCookerWord,
                    MealMarker.NewRecipe => NewRecipeWord,
                    _ => null,
                };
                return new RowFacts(dish.Title.Trim(), cook, kids, marker, null);
            case DinnerKind.EatOut:
                if (model.Outings is not null && model.Outings.TryGetValue(dinner.Day, out var outing))
                {
                    return new RowFacts($"Out · {outing.Place.Trim()}", $"Leave {TimeFormats.Clock12(outing.LeaveAt)}", null, EatingOutWord, null);
                }

                return new RowFacts("Out", null, null, EatingOutWord, dinner.Reason);
            case DinnerKind.Leftovers:
                return new RowFacts("Leftovers", null, null, null, dinner.Reason);
            default:
                return new RowFacts("Nothing planned", null, null, null, dinner.Reason);
        }
    }

    private static List<(PrintedRole Role, string Text)> Wrap(RowFacts facts, DinnerChoice dinner)
    {
        var lines = PageText.Wrap(facts.Title, DishChars).Select(line => (PrintedRole.Strong, line)).ToList();
        foreach (var small in new[] { facts.Cook, facts.Kids })
        {
            if (small is not null)
            {
                lines.AddRange(PageText.Wrap(small, SmallChars).Select(line => (PrintedRole.Small, line)));
            }
        }

        if (facts.Reason is not null)
        {
            var room = MaxRowLines - lines.Count;
            var words = PageText.Wrap(facts.Reason, SmallChars);
            lines.AddRange(words.Take(Math.Max(room, 0)).Select(line => (PrintedRole.Small, line)));
            if (words.Count > Math.Max(room, 0) && room > 0)
            {
                var last = lines.Count - 1;
                lines[last] = (PrintedRole.Small, lines[last].Item2.TrimEnd('.', ',') + "…");
            }
        }

        if (lines.Count > MaxRowLines)
        {
            throw new ArgumentException(
                $"{dinner.Day.DayOfWeek}'s dinner ('{facts.Title}') takes {lines.Count} lines and a row holds {MaxRowLines}; shorten the dish's name.",
                nameof(facts));
        }

        return lines;
    }
}
